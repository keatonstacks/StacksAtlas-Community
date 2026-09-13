using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.Logging;

namespace StacksAtlas.API.Filters;

// IDE0290: Primary Constructor
public class ApiExceptionFilter(ILogger<ApiExceptionFilter> logger, IHostEnvironment env) : IExceptionFilter
{
    public void OnException(ExceptionContext context)
    {
        var ex = context.Exception;
        var controller = context.RouteData.Values["controller"];
        var action = context.RouteData.Values["action"];

        logger.LogError(ex, "Unhandled exception in {Controller}.{Action}", controller, action);

        var statusCode = ex switch
        {
            ArgumentException => 400,
            UnauthorizedAccessException => 500,
            KeyNotFoundException => 404,
            _ => 500
        };

        context.Result = new ObjectResult(new
        {
            error = statusCode == 500 ? "An internal server error occurred." : ex.GetType().Name,
            message = ex.Message,
            detail = env.IsDevelopment() ? ex.StackTrace : null,
            path = context.HttpContext.Request.Path.Value
        })
        {
            StatusCode = statusCode
        };

        context.ExceptionHandled = true;
    }
}
