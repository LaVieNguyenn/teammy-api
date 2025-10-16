using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Teammy.Application.Common.Interfaces.Auth
{
public interface ITokenService
{
    string CreateAccessToken(Guid userId, string email, string displayName, string role, string? picture);
}
}