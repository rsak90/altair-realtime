using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using SasJobRunner.Services;

namespace SasJobRunner.Filters;

/// <summary>
/// Global async action filter that ensures a valid Bearer token is present in session
/// before allowing requests to proceed. Invokes TokenManager to acquire a token if needed.
/// Returns HTTP 503 if authentication fails.
/// </summary>
public sealed class BearerTokenRequiredFilter(ITokenManager tokenManager) : IAsyncActionFilter
{
    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        try
        {
            // Ensure valid token before action execution
            await tokenManager.EnsureValidTokenAsync(context.HttpContext.RequestAborted);
            
            // Token is valid, proceed with the request
            await next();
        }
        catch (InvalidOperationException ex)
        {
            // Authentication failed — return HTTP 503 Service Unavailable
            context.Result = new ObjectResult(new { error = "Authentication service unavailable", detail = ex.Message })
            {
                StatusCode = 503
            };
        }
    }
}
