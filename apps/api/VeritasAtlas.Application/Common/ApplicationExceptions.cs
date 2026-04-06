namespace VeritasAtlas.Application.Common;

public sealed class NotFoundException : Exception
{
    public NotFoundException(string message) : base(message)
    {
    }
}

public sealed class ConflictException : Exception
{
    public ConflictException(string message) : base(message)
    {
    }
}

public sealed class ValidationException : Exception
{
    public ValidationException(string message) : base(message)
    {
    }
}
