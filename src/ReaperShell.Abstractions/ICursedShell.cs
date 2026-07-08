namespace ReaperShell.Abstractions;

public interface ICursedShell
{
    bool IsEnabled { get; }

    int BlessingCharges { get; }

    int FailureChancePercent { get; }

    string Mood { get; }

    string? LastOmen { get; }

    void AddAmbientEvent(string message);

    void ShiftMood(string mood);

    void AddBlessing(int charges, string? reason = null);
}
