namespace HandelApp.Shared.Protocol;

public enum ResponseStatus
{
    Ok             = 0,
    AlreadyRunning = 1,
    NotRunning     = 2,
    Error          = 3,
    Unauthorized   = 4
}
