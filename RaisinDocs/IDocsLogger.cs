namespace RaisinDocs;

public enum DocsLogLevel
{
    Debug,
    Info,
    Warning,
    Error
}

public interface IDocsLogger
{
    bool IsDebugEnabled { get; }
    void Log(DocsLogLevel level, string message);
}
