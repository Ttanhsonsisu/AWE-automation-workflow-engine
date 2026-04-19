using AWE.Shared.Primitives;

namespace AWE.Shared.Extensions;

public static class ResultExtensions
{
    // Async support
    public static async Task<Result> BindAsync(
        this Result result,
        Func<Task<Result>> asyncBinder)
    {
        return result.IsSuccess
            ? await asyncBinder()
            : result;
    }

    public static async Task<Result<T>> BindAsync<T>(
        this Result<T> result,
        Func<T, Task<Result<T>>> asyncBinder)
    {
        return result.IsSuccess
            ? await asyncBinder(result.Value)
            : result;
    }

    public static async Task<Result<TResult>> MapAsync<T, TResult>(
        this Result<T> result,
        Func<T, Task<TResult>> asyncMapper)
    {
        return (Result<TResult>)(result.IsSuccess
            ? Result<TResult>.Success(await asyncMapper(result.Value))
            : Result<TResult>.Failure(result.Error!));
    }

    // Validation
    public static Result<T> Validate<T>(
        this Result<T> result,
        Func<T, bool> predicate,
        Error error)
    {
        if (result.IsFailure) return result;
        return (Result<T>)(predicate(result.Value)
            ? result
            : Result<T>.Failure(error));
    }

    // Combine multiple results
    public static Result Combine(params Result[] results)
    {
        var failed = results.FirstOrDefault(r => r.IsFailure);
        return failed ?? Result.Success();
    }

    public static Result<IEnumerable<T>> Combine<T>(params Result<T>[] results)
    {
        var errors = results.Where(r => r.IsFailure).Select(r => r.Error!).ToList();
        if (errors.Any())
        {
            var combinedError = Error.Failure("Multiple.Errors", "Multiple errors occurred")
                .WithMetadata("Errors", errors);
            return (Result<IEnumerable<T>>)Result<IEnumerable<T>>.Failure(combinedError);
        }

        return Result<IEnumerable<T>>.Success(results.Select(r => r.Value!));
    }

    // Try-catch to result
    public static Result<T> Try<T>(Func<T> func, string errorCode = "System.Error")
    {
        try
        {
            return Result<T>.Success(func());
        }
        catch (Exception ex)
        {
            return (Result<T>)Result<T>.Failure(Error.Unexpected(errorCode, ex.Message)
                .WithMetadata("Exception", ex.GetType().Name)
                .WithMetadata("StackTrace", ex.StackTrace));
        }
    }

    public static async Task<Result<T>> TryAsync<T>(
        Func<Task<T>> asyncFunc,
        string errorCode = "System.Error")
    {
        try
        {
            return Result<T>.Success(await asyncFunc());
        }
        catch (Exception ex)
        {
            return (Result<T>)Result<T>.Failure(Error.Unexpected(errorCode, ex.Message)
                .WithMetadata("Exception", ex.GetType().Name));
        }
    }

    // Convert from nullable
    public static Result<T> ToResult<T>(
        this T? value,
        Error errorIfNull)
    {
        return (Result<T>)(value is not null
            ? Result<T>.Success(value)
            : Result<T>.Failure(errorIfNull));
    }

    public static Result<T> ToResult<T>(
        this T? value,
        string errorCode = "Value.Null",
        string errorMessage = "Value cannot be null")
    {
        return (Result<T>)(value is not null
            ? Result<T>.Success(value)
            : Result<T>.Failure(Error.Validation(errorCode, errorMessage)));
    }
}
