using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Teammy.Application.Common.Results
{
    public sealed record OperationResult(bool Ok, string? Message = null, int StatusCode = 200)
    {
        public static OperationResult Success(string? message = null) => new(true, message, 200);
        public static OperationResult Fail(string message, int status = 400) => new(false, message, status);
    }
}
