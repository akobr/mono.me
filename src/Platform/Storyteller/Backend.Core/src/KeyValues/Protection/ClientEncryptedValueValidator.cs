using _42.Platform.Storyteller.KeyValues.ClientEncryption;

namespace _42.Platform.Storyteller.KeyValues.Protection;

// Checks a client-encrypted (Hyok) value without decrypting it: the format, the place, and the recipients.
public static class ClientEncryptedValueValidator
{
    public static string GetPlace(KeyValueIdentity identity)
    {
        return $"{identity.Organization}/{identity.Project}/{identity.View}/{identity.Key}";
    }

    // Returns the recipient ids the value was encrypted for.
    public static IReadOnlyList<string> Validate(string value, KeyValueIdentity identity, IReadOnlyList<ClientEncryptionRecipient> recipients)
    {
        ArgumentNullException.ThrowIfNull(identity);
        ArgumentNullException.ThrowIfNull(recipients);
        JweDocument document;

        try
        {
            document = JweDocument.Parse(value);
        }
        catch (FormatException exception)
        {
            throw new InvalidInputException($"The client-encrypted value is not valid: {exception.Message}", ErrorCodes.InvalidClientEncryptedValue);
        }

        if (!string.Equals(document.Enc, JweDocument.Encryption, StringComparison.Ordinal))
        {
            throw new InvalidInputException($"The client-encrypted value must use '{JweDocument.Encryption}'.", ErrorCodes.InvalidClientEncryptedValue);
        }

        var place = GetPlace(identity);

        if (!string.Equals(document.Place, place, StringComparison.Ordinal))
        {
            throw new InvalidInputException($"The client-encrypted value was made for '{document.Place}', not for '{place}'.", ErrorCodes.InvalidClientEncryptedValue);
        }

        var enabled = recipients.Where(recipient => recipient.IsEnabled).Select(recipient => recipient.Id).ToHashSet(StringComparer.Ordinal);
        var ids = new List<string>(document.Recipients.Count);

        foreach (var recipient in document.Recipients)
        {
            if (!string.Equals(recipient.Algorithm, JweDocument.Algorithm, StringComparison.Ordinal))
            {
                throw new InvalidInputException($"The recipient '{recipient.KeyId}' must use '{JweDocument.Algorithm}'.", ErrorCodes.InvalidClientEncryptedValue);
            }

            if (!enabled.Contains(recipient.KeyId))
            {
                throw new InvalidInputException($"'{recipient.KeyId}' is not an enabled recipient of the project.", ErrorCodes.UnknownRecipient);
            }

            ids.Add(recipient.KeyId);
        }

        return ids;
    }
}
