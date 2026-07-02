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
    void Log(DocsLogLevel level, string message);
}
