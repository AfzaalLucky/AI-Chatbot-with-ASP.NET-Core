using System.Net.Mail;
using Chatbot.Application.Common.Exceptions;

namespace Chatbot.Application.Validation;

/// <summary>Small collector so services can report every invalid field at once.</summary>
internal sealed class Validator
{
    private readonly Dictionary<string, List<string>> _errors = new(StringComparer.OrdinalIgnoreCase);

    public Validator Require(bool condition, string field, string error)
    {
        if (!condition)
        {
            if (!_errors.TryGetValue(field, out var list))
            {
                _errors[field] = list = [];
            }

            list.Add(error);
        }

        return this;
    }

    public void ThrowIfInvalid()
    {
        if (_errors.Count > 0)
        {
            throw new ValidationException(_errors.ToDictionary(e => e.Key, e => e.Value.ToArray()));
        }
    }

    public static bool IsValidEmail(string? email)
    {
        if (string.IsNullOrWhiteSpace(email) || email.Length > 256)
        {
            return false;
        }

        return MailAddress.TryCreate(email, out var address) && address.Address == email;
    }
}
