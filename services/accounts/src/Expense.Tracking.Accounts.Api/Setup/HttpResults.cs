using Expense.Tracking.Accounts.Domain.Common;

namespace Expense.Tracking.Accounts.Api.Setup;

public static class HttpResults
{
    public static int StatusCodeFor(ErrorType type) => type switch
    {
        ErrorType.Validation => StatusCodes.Status400BadRequest,
        ErrorType.NotFound => StatusCodes.Status404NotFound,
        ErrorType.Conflict => StatusCodes.Status409Conflict,
        ErrorType.Forbidden => StatusCodes.Status403Forbidden,
        _ => StatusCodes.Status400BadRequest
    };

    public static IResult Problem(Error error) =>
        Results.Problem(title: error.Code, detail: error.Description, statusCode: StatusCodeFor(error.Type));
}
