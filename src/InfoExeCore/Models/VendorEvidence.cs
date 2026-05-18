namespace InfoExeCore.Models;

public sealed record VendorEvidence(
    string EvidenceType,
    string VendorName,
    int Confidence
);