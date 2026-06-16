namespace AgenticRagScannerApi.Exceptions;

public class BadRequestException : Exception
{
    public BadRequestException(string message) : base(message)
    {
    }

    public BadRequestException(string message, Exception innerException) : base(message, innerException)
    {
    }
}

public class ConflictException : Exception
{
    public ConflictException(string message) : base(message)
    {
    }

    public ConflictException(string message, Exception innerException) : base(message, innerException)
    {
    }
}

public class ItemNotFoundException : Exception
{
    public ItemNotFoundException(string message) : base(message)
    {
    }

    public ItemNotFoundException(string message, Exception innerException) : base(message, innerException)
    {
    }
}

public class ValidationException : Exception
{
    public IDictionary<string, string[]> Errors { get; }

    public ValidationException(string message, IDictionary<string, string[]> errors) : base(message)
    {
        Errors = errors;
    }

    public ValidationException(string message, IDictionary<string, string[]> errors, Exception innerException)
        : base(message, innerException)
    {
        Errors = errors;
    }
}
