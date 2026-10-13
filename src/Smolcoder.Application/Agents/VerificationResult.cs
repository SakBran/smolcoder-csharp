namespace Smolcoder.Application.Agents;

public sealed class VerificationResult
{
    public VerificationResult(int attempts, bool passed, string output)
    {
        Attempts = attempts;
        Passed = passed;
        Output = output;
    }

    public int Attempts { get; }

    public bool Passed { get; }

    public string Output { get; }
}
