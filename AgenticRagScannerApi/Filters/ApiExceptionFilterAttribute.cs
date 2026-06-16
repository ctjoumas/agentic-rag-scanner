using AgenticRagScannerApi.Exceptions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;

namespace AgenticRagScannerApi.Filters;

/// <summary>
/// Global API exception filter that converts exceptions into consistent RFC problem responses.
/// </summary>
[ExcludeFromCodeCoverage]
public class ApiExceptionFilterAttribute : ExceptionFilterAttribute
{
    private readonly ILogger<ApiExceptionFilterAttribute> _logger;
    private readonly IDictionary<Type, Action<ExceptionContext>> _exceptionHandlers;

    public ApiExceptionFilterAttribute(ILogger<ApiExceptionFilterAttribute> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        _exceptionHandlers = new Dictionary<Type, Action<ExceptionContext>>
        {
            { typeof(BadRequestException), HandleBadRequestException },
            { typeof(ValidationException), HandleValidationException },
            { typeof(ItemNotFoundException), HandleItemNotFoundException },
            { typeof(UnauthorizedAccessException), HandleUnauthorizedAccessException },
            { typeof(ConflictException), HandleConflictException }
        };
    }

    public override void OnException(ExceptionContext context)
    {
        HandleException(context);
        base.OnException(context);
    }

    private void HandleException(ExceptionContext context)
    {
        var type = context.Exception.GetType();

        if (_exceptionHandlers.TryGetValue(type, out var handler))
        {
            handler(context);
            return;
        }

        if (!context.ModelState.IsValid)
        {
            HandleInvalidModelStateException(context);
            return;
        }

        HandleOtherException(context);
    }

    private void HandleBadRequestException(ExceptionContext context)
    {
        var exception = (BadRequestException)context.Exception;

        var details = CreateProblemDetails(
            type: "https://tools.ietf.org/html/rfc7231#section-6.5.1",
            title: "The request was invalid.",
            detail: exception.Message,
            status: StatusCodes.Status400BadRequest);

        _logger.LogError(exception, "{Title}. Exception: {Message}", details.Title, details.Detail);

        context.Result = new BadRequestObjectResult(details);
        context.ExceptionHandled = true;
    }

    private void HandleConflictException(ExceptionContext context)
    {
        var exception = (ConflictException)context.Exception;

        var details = CreateProblemDetails(
            type: "https://www.rfc-editor.org/rfc/rfc7231#section-6.5.8",
            title: "The requested operation caused a conflict.",
            detail: exception.Message,
            status: StatusCodes.Status409Conflict);

        _logger.LogError(exception, "{Title}. Exception: {Message}", details.Title, details.Detail);

        context.Result = new ConflictObjectResult(details);
        context.ExceptionHandled = true;
    }

    private void HandleItemNotFoundException(ExceptionContext context)
    {
        var exception = (ItemNotFoundException)context.Exception;

        var details = CreateProblemDetails(
            type: "https://tools.ietf.org/html/rfc7231#section-6.5.4",
            title: "The specified resource was not found.",
            detail: exception.Message,
            status: StatusCodes.Status404NotFound);

        _logger.LogError(exception, "{Title}. Exception: {Message}", details.Title, details.Detail);

        context.Result = new NotFoundObjectResult(details);
        context.ExceptionHandled = true;
    }

    private void HandleInvalidModelStateException(ExceptionContext context)
    {
        var exception = context.Exception;

        var details = new ValidationProblemDetails(context.ModelState)
        {
            Type = "https://tools.ietf.org/html/rfc7231#section-6.5.1",
            Status = StatusCodes.Status400BadRequest,
            Title = "One or more validation errors occurred."
        };
        AddCommonExtensions(details);

        _logger.LogError(exception, "{Title}. Exception: {Message}", details.Title, exception.Message);

        context.Result = new BadRequestObjectResult(details);
        context.ExceptionHandled = true;
    }

    private void HandleUnauthorizedAccessException(ExceptionContext context)
    {
        var exception = context.Exception;

        var details = CreateProblemDetails(
            type: "https://tools.ietf.org/html/rfc7235#section-3.1",
            title: "Unauthorized",
            detail: exception.Message,
            status: StatusCodes.Status401Unauthorized);

        _logger.LogError(exception, "{Title}. Exception: {Message}", details.Title, details.Detail);

        context.Result = new ObjectResult(details)
        {
            StatusCode = StatusCodes.Status401Unauthorized
        };

        context.ExceptionHandled = true;
    }

    private void HandleValidationException(ExceptionContext context)
    {
        var exception = (ValidationException)context.Exception;

        var details = new ValidationProblemDetails(exception.Errors)
        {
            Type = "https://tools.ietf.org/html/rfc7231#section-6.5.1",
            Status = StatusCodes.Status400BadRequest,
            Title = "Validation failed."
        };
        AddCommonExtensions(details);

        _logger.LogError(exception, "{Title}. Exception: {Message}", details.Title, exception.Message);

        context.Result = new BadRequestObjectResult(details);
        context.ExceptionHandled = true;
    }

    private void HandleOtherException(ExceptionContext context)
    {
        var exception = context.Exception;

        var details = CreateProblemDetails(
            type: "https://datatracker.ietf.org/doc/html/rfc7231#section-6.6.1",
            title: "Handled internal exception",
            detail: exception.Message,
            status: StatusCodes.Status500InternalServerError);

        _logger.LogError(exception, "{Title}. Exception: {Message}", details.Title, details.Detail);

        context.Result = new ObjectResult(details)
        {
            StatusCode = StatusCodes.Status500InternalServerError
        };

        context.ExceptionHandled = true;
    }

    private static ProblemDetails CreateProblemDetails(string type, string title, string detail, int status)
    {
        var details = new ProblemDetails
        {
            Type = type,
            Title = title,
            Detail = detail,
            Status = status
        };
        AddCommonExtensions(details);

        return details;
    }

    private static void AddCommonExtensions(ProblemDetails details)
    {
        details.Extensions["traceId"] = Activity.Current?.Id;
        details.Extensions["timestamp"] = DateTimeOffset.UtcNow;
    }
}
