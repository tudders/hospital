namespace Alcidion.Patients.Application;

public sealed record RegisterPatientCommand(string Mrn, string GivenName, string FamilyName, DateOnly DateOfBirth);
