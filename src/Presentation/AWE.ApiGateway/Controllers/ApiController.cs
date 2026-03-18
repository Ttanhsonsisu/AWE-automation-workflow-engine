using AWE.Shared.Primitives;
using Microsoft.AspNetCore.Mvc;

namespace AWE.ApiGateway.Controllers;

[ApiController]
public abstract class ApiController : ControllerBase
{
    /// <summary>
    /// process result 
    /// </summary>
    protected IActionResult HandleResult<T>(Result<T> result)
    {
        if (result.IsSuccess)
        {
            return Ok(new
            {
                success = true,
                data = result.Value
            });
        }

        return HandleFailure(result.Error!);
    }

    /// <summary>
    /// (Result void)
    /// </summary>
    protected IActionResult HandleResult(Result result)
    {
        if (result.IsSuccess)
        {
            return Ok();
        }

        return HandleFailure(result.Error!);
    }

    /// <summary>
    /// Convert ErrorType to HTTP Status Code
    /// </summary>
    protected IActionResult HandleFailure(Error error)
    {
        var response = new
        {
            error.Code,
            error.Message,
            error.Type
        };

        return error.Type switch
        {
            ErrorType.Validation => BadRequest(response),
            ErrorType.NotFound => NotFound(response),
            ErrorType.Conflict => Conflict(response),
            ErrorType.Unauthorized => Unauthorized(response),
            ErrorType.Forbidden => Forbid(),
            ErrorType.Failure => StatusCode(500, response), 
            _ => StatusCode(500, response)
        };
    }
}
