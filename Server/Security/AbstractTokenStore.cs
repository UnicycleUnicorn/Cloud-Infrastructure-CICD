﻿using Common.Logging;
using Microsoft.IdentityModel.Tokens;
using System.Diagnostics.CodeAnalysis;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;

namespace Server.Security;

/// <summary>
/// Object to generate, retrieve, renew, verify, and destroy tokens relating to security (JWT & refresh)
/// </summary>
public abstract class AbstractTokenStore
{
    private readonly TimeSpan DefaultAuthorizationExpiration;
    private readonly TimeSpan DefaultRefreshExpiration;
    private readonly TimeSpan ClockSkew;

    public AbstractTokenStore(TimeSpan defaultAuthoriationExpiration, TimeSpan defaultRefreshExpiration, TimeSpan clockSkew)
    {
        DefaultAuthorizationExpiration = defaultAuthoriationExpiration;
        DefaultRefreshExpiration = defaultRefreshExpiration;
        ClockSkew = clockSkew;
    }

    public string GenerateAuthorizationToken(string username, string[]? roles) => GenerateAuthorizationToken(username, roles, DefaultAuthorizationExpiration);
    public string GenerateAuthorizationToken(string username, string[]? roles, TimeSpan expiration)
    {
        var claims = new List<Claim>
        {
            new Claim(JwtRegisteredClaimNames.Sub, username), // Subject (typically the user's identifier)
            new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()), // Unique token identifier
            new Claim(JwtRegisteredClaimNames.Iss, Config.AuthIssuer), // Issuer
            new Claim(JwtRegisteredClaimNames.Aud, Config.AuthAudience), // Audience
        };

        if (roles != null)
        {
            foreach (string role in roles)
            {
                claims.Add(new Claim(ClaimTypes.Role, role));
            };
        }

        var tokenHandler = new JwtSecurityTokenHandler();
        var tokenDescriptor = new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity(claims),

            Expires = DateTime.UtcNow.Add(expiration), // Token expiration time

            SigningCredentials = SecurityHandler.AuthorizationSigningCredentials
        };

        SecurityToken token = tokenHandler.CreateToken(tokenDescriptor);

        LogWriter.LogInfo("Generated new authorization token");

        return tokenHandler.WriteToken(token);
    }

    public string GenerateRefreshToken(string username) => GenerateRefreshToken(username, DefaultRefreshExpiration);
    public string GenerateRefreshToken(string username, TimeSpan expiration)
    {
        string token = Guid.NewGuid().ToString();
        _ = StoreRefreshToken(token, username, DateTime.UtcNow.Add(expiration));

        LogWriter.LogInfo("Generated new refresh token");

        return token;
    }

    public (string authorizationToken, string refreshToken) GenerateTokenSet(string username, string[] roles) => (GenerateAuthorizationToken(username, roles), GenerateRefreshToken(username));

    public bool RemoveAndVerifyRefreshToken(string token, [NotNullWhen(true)] out string? username, [NotNullWhen(true)] out string? newRefreshToken)
    {
        (string token, string username, DateTime expiration)? removed = RemoveRefreshToken(token);

        if (removed.HasValue)
        {
            if (removed.Value.expiration <= DateTime.UtcNow.Add(ClockSkew))
            {
                username = removed.Value.username;
                newRefreshToken = GenerateRefreshToken(username);
                return true;
            }
        }

        username = null;
        newRefreshToken = null;
        return false;
    }

    public abstract void RemoveRelatedRefreshTokens(string username);

    public abstract bool StoreRefreshToken(string token, string username, DateTime expiration);
    public abstract (string token, string username, DateTime expiration)? RemoveRefreshToken(string token);
    public abstract void BlacklistAuthorizationToken(string jwt);
    public abstract bool IsAuthorizationBlacklisted(string jwt);
}
