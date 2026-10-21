import { pair, secretReferenceContentType, type StoredKeyValue } from '../key-values'

// The values store of some showcase projects (424-77): the settings and secrets their services read at run time,
// per environment. Only the Public and Standard levels, which every project allows; Key Vault secrets stay in the
// vault and are listed as references.

// key-values.ts imports this module, so its exports are read when a scope is seeded, not at import time.
const json = { contentType: 'application/json' }
const reference = () => ({ contentType: secretReferenceContentType })

const scopes: Record<string, () => StoredKeyValue[]> = {
  'fabrikam/finance-cloud/default': () => [
    pair('smtp.host', 'Public', ['smtp.fabrikam.example', 'smtp.eu.fabrikam.example']),
    pair('smtp.password', 'Standard', ['Fc-smtp-2026-01', 'Fc-smtp-2026-07']),
    pair('tax.avalara.account-id', 'Public', ['2000148912']),
    pair('tax.avalara.license-key', 'Standard', ['AV-9F3K-22LM-81QZ']),
    pair('bank-sync.plaid.secret', 'Public', ['kv://fabrikam-prod/plaid-secret'], reference()),
    pair('features.flags', 'Public', ['{"aiReconciliation":false}', '{"aiReconciliation":false,"ksef":true}'], json),
    pair('legacy.sftp-host', 'Public', ['sftp.old.fabrikam.example'], { deletedDaysAgo: 12 }),
  ],
  'fabrikam/finance-cloud/staging': () => [
    pair('smtp.host', 'Public', ['smtp.staging.fabrikam.example']),
    pair('smtp.password', 'Standard', ['Fc-smtp-staging']),
    pair('features.flags', 'Public', ['{"aiReconciliation":true,"ksef":true}'], json),
  ],
  'fabrikam/fitness-cloud/default': () => [
    pair('payments.stripe.publishable-key', 'Public', ['pk_live_51FitCloud0aB9']),
    pair('payments.stripe.secret-key', 'Standard', ['sk_live_51FitCloud7xQ2', 'sk_live_51FitCloud9rT4']),
    pair('push.fcm.server-key', 'Public', ['kv://fabrikam-fitness/fcm-key'], reference()),
    pair('booking.max-advance-days', 'Public', ['14', '21']),
    pair('mail.sender', 'Public', ['members@fitness-cloud.example']),
  ],
  'streamwave/streaming-platform/default': () => [
    pair('cdn.signing-key', 'Standard', ['cdn-k1-7d2e', 'cdn-k2-91aa', 'cdn-k3-4c0f']),
    pair('drm.widevine.provider', 'Public', ['streamwave']),
    pair('drm.license-server', 'Public', ['https://license.streamwave.example/widevine']),
    pair('recommendations.model', 'Public', ['reco-v7', 'reco-v8']),
    pair('playback.ladder', 'Public', ['{"maxHeight":2160,"codecs":["avc","hevc"]}'], json),
    pair('billing.adyen.api-key', 'Public', ['kv://streamwave-prod/adyen-api-key'], reference()),
  ],
  'streamwave/streaming-platform/release-candidate': () => [
    pair('recommendations.model', 'Public', ['reco-v9']),
    pair('playback.ladder', 'Public', ['{"maxHeight":2160,"codecs":["avc","hevc","av1"]}'], json),
  ],
  'harbor-industrial/plant-fleet/default': () => [
    pair('mqtt.broker', 'Public', ['mqtts://mqtt.harbor-industrial.example:8883']),
    pair('mqtt.device-password', 'Standard', ['hp-mqtt-24', 'hp-mqtt-25']),
    pair('historian.url', 'Public', ['https://historian.harbor-industrial.example']),
    pair('alerts.pager-key', 'Public', ['kv://harbor-ops/pager-routing-key'], reference()),
    pair('control-loop.max-pressure-bar', 'Public', ['10']),
  ],
  'seabreeze-hotels/guest-platform/default': () => [
    pair('channel.booking-com.hotel-ids', 'Public', ['{"lisbon-marina":"4410231","porto-riverside":"4410298"}'], json),
    pair('channel.booking-com.password', 'Standard', ['Sb-bdc-2026']),
    pair('channel.expedia.api-key', 'Public', ['kv://seabreeze/expedia-api-key'], reference()),
    pair('mobile-key.assa-abloy.tenant', 'Public', ['seabreeze-eu']),
    pair('pms.endpoint', 'Public', ['https://pms.seabreeze.example/api', 'https://pms2.seabreeze.example/api']),
  ],
}

/** The seeded pairs of a showcase scope ("org/project/view"); none for the others. */
export const showcaseKeyValues = (scopeId: string): StoredKeyValue[] => scopes[scopeId]?.() ?? []
