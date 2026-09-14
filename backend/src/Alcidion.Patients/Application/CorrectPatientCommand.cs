namespace Alcidion.Patients.Application;

/// <summary>
/// What a correction changes. Every field is optional and null means "leave this alone", so the
/// command carries only what is being corrected - which is what lets two clerks fix two different
/// fields without either of them reinstating the other's old value.
/// </summary>
public sealed record CorrectPatientCommand(string? Mrn, string? GivenName, string? FamilyName, DateOnly? DateOfBirth);
