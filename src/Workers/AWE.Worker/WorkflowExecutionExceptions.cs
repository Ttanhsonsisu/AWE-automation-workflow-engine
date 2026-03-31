namespace AWE.Worker;

public class RetryableException : Exception
{
    public RetryableException(string message, Exception? innerException = null)
        : base(message, innerException)
    {
    }
}

public class NonRetryableException : Exception
{
    public NonRetryableException(string message, Exception? innerException = null)
        : base(message, innerException)
    {
    }
}
