using Microsoft.AspNetCore.Identity;
using Mone.Infrastructure.Data.Entities;

namespace Mone.Api.Services;

public class ExternalAuthProvisioningService
{
    private readonly UserManager<UserEntity> _userManager;

    public ExternalAuthProvisioningService(UserManager<UserEntity> userManager)
    {
        _userManager = userManager;
    }

    public async Task<UserEntity> FindOrCreateExternalUserAsync(
        string email,
        string authProvider,
        string externalId,
        string? displayName)
    {
        var user = await _userManager.FindByEmailAsync(email);
        if (user is not null)
        {
            user.AuthProvider = authProvider;
            user.ExternalId = externalId;
            if (displayName is not null)
                user.DisplayName = displayName;
            await _userManager.UpdateAsync(user);
            return user;
        }

        user = new UserEntity
        {
            UserName = email,
            Email = email,
            AuthProvider = authProvider,
            ExternalId = externalId,
            DisplayName = displayName
        };

        var result = await _userManager.CreateAsync(user);
        if (!result.Succeeded)
        {
            throw new InvalidOperationException(
                $"Failed to create external user {email}: {string.Join("; ", result.Errors.Select(e => e.Description))}");
        }

        return user;
    }
}
