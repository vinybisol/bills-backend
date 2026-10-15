using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Application.Abstractions.Services;
using Application.DTOs.Factories;
using Application.DTOs.Services.PesonAccess;
using Domain.Abstractions;
using Domain.Infrastructures;
using Microsoft.Extensions.Options;

namespace Application.Services;

public class SharedPagesTokenService(
    IOptions<SharedPagesOptions> sharedPagesOptions,
    TimeProvider timeProvider) : ISharedPagesTokenService
{
    public Result<SharedPagesTokenDto> Issue(long personId, DateTimeOffset? expiresAt)
    {
        var secret = sharedPagesOptions.Value.Secret;
        var secretBytes = Encoding.UTF8.GetBytes(secret);

        var tokenId = Guid.NewGuid();
        var payload = new PesonAccessLinkTokenDto(personId, tokenId, expiresAt);

        var json = JsonSerializer.Serialize(payload);
        var jsonBytes = Encoding.UTF8.GetBytes(json);

        var hashBytes = HMACSHA256.HashData(secretBytes, jsonBytes);
        var signature = Convert.ToHexStringLower(hashBytes);

        var jsonForToken = Base64Url.EncodeToString(jsonBytes);
        var tokemFromUser = $"{jsonForToken}.{signature}";
        return new SharedPagesTokenDto(tokenId, tokemFromUser, expiresAt);
    }

    public Result<PesonAccessLinkTokenDto> Validate(string token)
    {
        var secret = sharedPagesOptions.Value.Secret;
        var secretBytes = Encoding.UTF8.GetBytes(secret);

        var parts = token.Split('.');
        if (parts.Length != 2)
            return Error.Validation("token is not a valid format");

        var payload = parts[0];
        var signature = parts[1];

        var payloadBytes = Base64Url.DecodeFromChars(payload);

        var sigToValidata = HMACSHA256.HashData(secretBytes, payloadBytes);
        var newSignature = Convert.ToHexStringLower(sigToValidata);

        var assinaturaValida = CryptographicOperations.FixedTimeEquals(
            Convert.FromHexString(signature),
            Convert.FromHexString(newSignature)
        );
        if (assinaturaValida is false)
            return Error.InvalidOperation;

        var pesonAccessLinkTokenDto = JsonSerializer.Deserialize<PesonAccessLinkTokenDto>(payloadBytes);

        if (pesonAccessLinkTokenDto is null)
            return Error.InvalidOperation;

        if (pesonAccessLinkTokenDto.ExpiresAt < timeProvider.GetUtcNow())
            return Error.InvalidOperation;

        return pesonAccessLinkTokenDto;
    }
}
