# Access Audit

Every change of who can access what writes an **access audit record**: who changed it (an [`AuditActor`](audit-stamps.md#the-actor), with `Via` when an [actor](access.md#actors) such as the MCP server acted for the person), when, what, on which organization or project, to whom, the value before and after, and the correlation id of the request. The record is written in the same Cosmos DB transaction as the change, and nothing updates or deletes it; it expires after the retention. The design and its alternatives are in [the spec](specs/2026-11-15%20Append-Only%20Access%20Audit%20Trail.md).

## What is recorded

| Action | Change | Target | Old → new value |
| --- | --- | --- | --- |
| `MemberGranted` | `POST v1/access/grant`, also when it raises a role | the account | the role before (none for a new member) → the role |
| `MemberRevoked` | `POST v1/access/revoke` | the account | the role → none |
| `MemberRoleChanged` | `PUT v1/access/points/{key}/members/{accountId}` | the account | the role before → the role |
| `MemberRemoved` | `DELETE v1/access/points/{key}/members/{accountId}`; leaving is actor = target | the account | the role → none |
| `MemberJoined` | accepting an invitation (`Detail`: the invitation and who sent it); the invitee is the actor | the account | the role before → the role |
| `AccessPointCreated` | a new organization or project (`POST v1/access/points`, `POST v1/access/account` with a project, DbCreator); creating the first project of a new organization creates two access points and records two | the owner | → `Owner` (`Detail`: the project's description) |
| `AccessPointUpdated` | `PUT v1/access/points/{key}` | | the description before → after |
| `MachineCreated` | `POST …/access/machines` | the machine | → kind, scopes, annotation restriction, shared certificate, thumbprint |
| `MachineReset` | `PUT …/access/machines/{id}` | the machine | → the kind (`Detail`: the API key or the client secret was replaced) |
| `MachineDeleted` | `DELETE …/access/machines/{id}` | the machine | the kind, scopes, … → none |
| `MachineFederationRulesChanged` | `PUT …/access/machines/{id}/federation-rules` | the machine | the rules before → after |
| `MachineAuthenticationPolicyChanged` | `PUT v1/access/points/{key}/machine-authentication` | | the policy before → after |
| `KeyValuePolicyChanged` | the key/value policy of a project: its levels, default level, mTLS rule, HSM backend and rotation interval, customer key, and recipients | | the access view before → after |
| `FederatedIssuerCreated`, `FederatedIssuerUpdated`, `FederatedIssuerDeleted` | `PUT` and `DELETE …/access/issuers/{name}` | the issuer | issuer, key source, JWKS URI, number of JWKS keys and a thumbprint of the key set (its members by name, so a reordered set is the same), lifetime, required claims, description |
| `SharedCertificateIssued`, `SharedCertificateRevoked` | `POST` and `DELETE …/access/certificates[/{thumbprint}]` | the certificate (thumbprint, label) | label, not before, not after, revoked |

A change that changes nothing records nothing, as a membership that is already what the request asks for: a grant of a role the account has or exceeds, the same role set again, the same description, policy, rules or issuer saved again, a certificate revoked twice. The key/value policy is compared on its **access view** only (`AccessAuditValues.KeyValuePolicy`, the allowed levels as a set): key versions, key health, verifications, rotations and rewrap progress are not access changes, so the maintenance runs record nothing here; the [key/value audit](key-values.md#audit-trail) keeps recording them.

DbCreator records as `System`: the access points its base seeding creates or changes (each membership is one batch of the access point, the account and the record; a role only the access point still holds is repaired on the account and recorded), and the projects and owners its [data sets](data-sets.md) create.

Not recorded: registering an account without a project (it gives no access), invitations (an invitation keeps its sender and status, and its acceptance is the `MemberJoined` record), and a machine renewing its own certificate over mTLS (it changes no access).

**Never stored**: an API key, a client secret, a certificate or its password, a hash, the stored mask of a key (it keeps the first characters of a client secret), a JWKS key (only how many, and a thumbprint of the set, so a key replaced by another is a change) and a recipient's public key (only its id, name, state and a thumbprint of the key). A value longer than 4,000 characters is cut and ends with `… (truncated, sha256 <16 hex digits>)`, so a full copy can still be matched.

## Where the records are, and why the change and its record are atomic

A record lives in the partition of the change it records, and is the last item of the same `TransactionalBatch`. A failed batch stores neither, and there is no write path for a record alone.

| Changes | Partition |
| --- | --- |
| memberships, access points, both policies of a project, DbCreator's seeding | `core`, partition `access`, next to the accounts and the access points |
| machines, shared certificates | `organizations`, (organization, `{project}.access`), next to the machines |
| trusted issuers | `organizations`, (organization, `{project}.federation`), next to the issuers |

The record's `id` is `aca.{yyyyMMddHHmmssfffffff}.{guid}`, so the order of the ids is the order of the times. It carries `Organization`, `Project` (none for an organization) and `AccessPointKey`, and a `ttl`.

- **Memberships** replace the access point and the account in one batch (see [access.md](access.md)); the record is its third item.
- **Creating an organization or a project** writes the access points, the owner's account (created, or replaced with its ETag) and their records in one batch, so a failure leaves nothing half created; a concurrent creation answers `409`.
- **A machine's reset**: for the kinds with an API key, the change is the new hash, which `CosmosMergedApiKeyHashStore` patches several layers below the access service. The access service opens the record of the reset as a pending, ambient record (`PendingAccessAudit`, like a `TransactionScope`), and the hash store patches the hash in a batch with it. For `ClientCredentials`, the identity provider replaces the secret first, outside any transaction of Storyteller; the mask is then patched in a batch with the record, and the new secret is returned only after both are stored. A failed batch answers `503` with `ErrorCode` `AccessAuditUnavailable`: the old secret no longer works and the new one never leaves, so the caller resets the machine again.
- **A machine's creation** writes the full machine and its record in one batch, after the provider stored the credential (the API key's hash, the certificate, the identity provider's application); the credential is returned only after both. A failed batch then removes the credential again, best effort, and the request fails. Nothing is returned to the caller and nothing is recorded, but something can stay behind:
  - **even when the cleanup reported no error**: the hash store wrote the API key's hash on a partial machine document before the batch, and the cleanup only clears the hash, so the partial machine stays listed (`AccessKey` `***`, no `CredentialKind`, the requested scopes); and the cleanup's revocation of a certificate only logs a failure. So after a failed creation, check the project's machines (`GET …/access/machines`): delete a partial machine you confirm is one (`DELETE …/access/machines/{id}`), and check its certificate records as below;
  - **when the cleanup failed too**: an API key's hash on the partial machine document (deleting the machine id removes it, as above); a certificate record (`mcert.{thumbprint}`, its `MachineAccessId` the machine id) without a machine document, which deleting the machine id does not reach (the delete answers `404` when there is no machine document); an application at the identity provider, removed at the identity provider.

  A certificate record that stays behind is not listed, and its certificate and password were never returned, so nobody can present it; Cosmos DB removes it by its `ttl`, 30 days after the certificate's `NotAfter`. To retire it sooner, find the `mcert.` items with that `MachineAccessId` in the project's `{project}.access` partition, and revoke them in the certificate store by the machine id (`IClientCertificateStore.RevokeAsync`, which marks every `mcert.` record of the machine revoked) or delete them.
- **A machine's delete** removes the credential first, as before, then deletes the document in a batch with the record. A delete that fails after the credential is gone leaves the machine listed, and is recorded when it is retried. For a `Certificate` or `CertificateAndApiKey` machine with its own certificate, the revocation of its `mcert.` record is best effort: a failed revocation is only logged (`Failed to revoke certificate for {ObjectId}`), and the delete still answers `200` and records `MachineDeleted`. After such a warning, handle the records by the machine's `MachineAccessId` as above. A machine on a shared certificate (`UsesSharedCertificate`) has no `mcert.` record of its own: the shared certificate (`scr.`) stays until it is revoked through `DELETE …/access/certificates/{thumbprint}`.
- **A trusted issuer's delete** deletes the version it read (with its ETag) and reads it again once on a concurrent change, so the record describes what was deleted.
- **The machine authentication policy** is replaced with its ETag (it used to be an upsert without one, which could lose a membership changed at the same moment) and retried once on a concurrent change.

The partitions that hold records also hold what the API lists, so the lists skip them: the machines of a project (`GET …/access/machines` also skips the `mcert.` certificate records and the `scr.` shared certificates) and the trusted issuers, whose count of `FederationValidation.MaxIssuersPerProject` they do not take part in.

## Reading the trail

| Method | Route | Needs | Result |
| --- | --- | --- | --- |
| GET | `v1/access/points/{key}/audit` | `Administrator` or `Owner` on the access point, users only | `AccessAuditResponse`: `Items` (`AccessAuditEntry[]`), `ContinuationToken`, `Count` |

- `{key}` is an organization: its whole trail, memberships of the organization and every project's records. `{key}` is `{organization}.{project}`: the project's records only, for the project's Owners and Administrators. The role is the exact role on the key, as for the members: an organization's Owner reads a project's records through the organization key with `?project=`.
- Filters, all optional: `project` (only that project; the organization's own records are left out; with a project key it must be that project, else `400`), `action` (an `AccessAuditAction`, any case), `target` (an account id, a machine id, an issuer's name or a certificate's thumbprint), `from` and `to` (ISO 8601, both inclusive).
- Newest first, at most 100 per page. Pass `ContinuationToken` back as `continuationToken` until it is missing. The token names the last record of the page, so the next page is the records just older than it in both partitions together: no record is skipped or repeated, and records written meanwhile appear on a new first page. The token is not tied to the filters: send the same filters with it. The ids come from the clock of the API instance that wrote them, so records written by two instances within the difference of their clocks can come in another order than they happened.
- `400` with `ErrorCode` `InvalidQuery` for a filter that does not parse, a project that is not the key's, a `from` later than `to`, and a continuation token that is not one of this trail. `403` (`AccessDenied`) for a machine and for every role below `Administrator`.
- `IAccessAuditLog.QueryAsync` (`CosmosAccessAuditLog`) runs one query per source, `core` (partition `access`, filtered on `c.Organization`) and `organizations` (the organization as the key prefix, filtered on `c.Organization`), each `TOP` one more than the page and ordered by id, and merges them. There is no update or delete route, nor a service method that changes a record.

The SDKs have it as `GetAccessAuditAsync` (`IAccessApiClient`, NSwag) and `getAccessAudit` / `useGetAccessAudit` (TypeScript). The admin UI shows the trail of an organization on its **Audit** page.

## Retention

| Setting | Default | Meaning |
| --- | --- | --- |
| `AccessAudit:RetentionDays` | `730` (two years) | Days a record is kept; between 1 and 3650, checked on start |

Cosmos DB removes a record when its `ttl` runs out. This needs TTL on the container: `organizations` has it, and DbCreator turns it on for `core` too (`DefaultTimeToLive = -1`, no default expiry, so nothing else in `core` expires) when it creates the container and, for an existing one, on every start. Until DbCreator has run once after the deployment of this feature, the records in `core` do not expire; the retention can only get longer, never shorter.

**Size.** The `access` partition of `core` is one logical partition of the whole platform, and a logical partition holds at most 20 GB. A record of a membership is about 1 KB; one of a policy, an issuer or federation rules holds the value before and after, up to 4,000 characters each, so up to about 9 KB. 1,000 access changes a day for two years, mostly memberships, are about 1 GB next to the accounts and access points. An archive of the records (an export to Blob Storage with an immutability policy) is a follow-up.
