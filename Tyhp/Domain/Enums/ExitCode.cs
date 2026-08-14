namespace Tyhp.Domain.Enums
{
    public enum ExitCode
    {
        Success = 0,
        GenericError = 1,
        IntegrityCheckFailed = 2,
        InvalidAction = 3,
        CompileError = 4,
        CompileWarning = 5,
    }
}