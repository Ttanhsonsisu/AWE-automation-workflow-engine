namespace AWE.Shared.Primitives;

public class Result
{
    public bool IsSuccess { get; }
    public bool IsFailure => !IsSuccess;
    public Error? Error { get; }

    protected Result(bool isSuccess, Error? error = null)
    {
        IsSuccess = isSuccess;
        Error = error;
    }

    // Success
    public static Result Success() => new(true);
    public static Result<T> Success<T>(T value) => new(value);

    // Failure
    public static Result Failure(Error error) => new(false, error);
    public static Result<T> Failure<T>(Error error) => new(default, error);
    public static Result Failure(string code, string message, ErrorType type = ErrorType.Failure)
        => Failure(Error.Failure(code, message).WithMetadata("Type", type));

    // Pattern matching
    public T Match<T>(Func<T> onSuccess, Func<Error, T> onFailure)
        => IsSuccess ? onSuccess() : onFailure(Error!);

    public void Match(Action onSuccess, Action<Error> onFailure)
    {
        if (IsSuccess) onSuccess();
        else onFailure(Error!);
    }

    // Transformations
    public Result<T> Map<T>(Func<T> mapper)
        => IsSuccess ? Success(mapper()) : Failure<T>(Error!);

    public Result Bind(Func<Result> binder)
        => IsSuccess ? binder() : this;

    public Result<T> Bind<T>(Func<Result<T>> binder)
        => IsSuccess ? binder() : Failure<T>(Error!);

    // Side effects
    public Result Tap(Action action)
    {
        if (IsSuccess) action();
        return this;
    }

    public Result TapError(Action<Error> action)
    {
        if (IsFailure) action(Error!);
        return this;
    }

    public static implicit operator Result(Error error) => Failure(error);
}

public class Result<T> : Result
{
    private readonly T? _value;

    public T Value => IsSuccess ? _value! : throw new InvalidOperationException("Cannot access value of failed result");
    public T? ValueOrDefault => _value;

    internal Result(T value) : base(true)
    {
        _value = value;
    }

    internal Result(T? value, Error error) : base(false, error)
    {
        _value = value;
    }

    // Pattern matching
    public TResult Match<TResult>(Func<T, TResult> onSuccess, Func<Error, TResult> onFailure)
        => IsSuccess ? onSuccess(_value!) : onFailure(Error!);

    public void Match(Action<T> onSuccess, Action<Error> onFailure)
    {
        if (IsSuccess) onSuccess(_value!);
        else onFailure(Error!);
    }

    // Transformations
    public Result<TResult> Map<TResult>(Func<T, TResult> mapper)
        => IsSuccess ? Success(mapper(_value!)) : Failure<TResult>(Error!);

    public Result<TResult> Bind<TResult>(Func<T, Result<TResult>> binder)
        => IsSuccess ? binder(_value!) : Failure<TResult>(Error!);

    // Side effects
    public Result<T> Tap(Action<T> action)
    {
        if (IsSuccess) action(_value!);
        return this;
    }

    // LINQ support
    public Result<TResult> Select<TResult>(Func<T, TResult> selector) => Map(selector);

    public Result<TResult> SelectMany<TResult>(
        Func<T, Result<TResult>> selector) => Bind(selector);

    // Conversion
    public static implicit operator Result<T>(T value) => Success(value);
    public static implicit operator Result<T>(Error error) => Failure<T>(error);
}
