namespace ReaperShell.Shell;

public interface ICurseRandom
{
    int Next(int maxExclusive);
}

public sealed class CurseRandom : ICurseRandom
{
    public int Next(int maxExclusive)
    {
        return Random.Shared.Next(maxExclusive);
    }
}
