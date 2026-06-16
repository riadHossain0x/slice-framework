using Slice.Domain.Guards;
using Slice.Domain.Values;

namespace Slice.Sample.Crm.Domain.Leads;

public sealed class FullName : ValueObject
{
    public string FirstName { get; }
    public string LastName { get; }

    private FullName(string firstName, string lastName)
    {
        FirstName = firstName;
        LastName = lastName;
    }

    public static FullName Create(string firstName, string lastName)
    {
        Ensure.NotNullOrWhiteSpace(firstName, nameof(firstName), 128);
        Ensure.NotNullOrWhiteSpace(lastName, nameof(lastName), 128);
        return new FullName(firstName.Trim(), lastName.Trim());
    }

    public string DisplayName => $"{FirstName} {LastName}";

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return FirstName;
        yield return LastName;
    }
}

public sealed class ContactInfo : ValueObject
{
    public string? Email { get; }
    public string? Phone { get; }

    private ContactInfo(string? email, string? phone)
    {
        Email = email;
        Phone = phone;
    }

    public static ContactInfo Create(string? email, string? phone)
    {
        Ensure.True(
            !string.IsNullOrWhiteSpace(email) || !string.IsNullOrWhiteSpace(phone),
            "A lead must have an email or a phone number.",
            "Crm:Lead.ContactRequired");
        return new ContactInfo(email?.Trim().ToLowerInvariant(), phone?.Trim());
    }

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return Email;
        yield return Phone;
    }
}
