using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.Options;

namespace LankaMart.Api.Common;

/// <summary>
/// Makes an action return 404 unless the app is running in Development AND
/// LankaMart:EnableTeachingEndpoints is true.
/// </summary>
/// <remarks>
/// The Lecture 03 demo documented a flag like this but never enforced it. Here
/// it is enforced, because the endpoints it guards include a deliberately
/// SQL-injectable query. A teaching example that is dangerous when deployed is
/// exactly the kind of thing that ends up deployed - so the guard is code, not
/// a comment. Two independent conditions, either of which is enough to close it.
/// </remarks>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public sealed class TeachingEndpointAttribute : Attribute, IActionFilter
{
    public void OnActionExecuting(ActionExecutingContext context)
    {
        var services = context.HttpContext.RequestServices;
        var env      = services.GetRequiredService<IHostEnvironment>();
        var options  = services.GetRequiredService<IOptions<LankaMartOptions>>().Value;

        if (!env.IsDevelopment() || !options.EnableTeachingEndpoints)
            context.Result = new NotFoundResult();
    }

    public void OnActionExecuted(ActionExecutedContext context) { }
}
