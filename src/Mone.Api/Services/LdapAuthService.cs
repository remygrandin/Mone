using Novell.Directory.Ldap;

namespace Mone.Api.Services;

public sealed record LdapAuthResult(bool Success, string? Email, string? DisplayName, string? DistinguishedName);

public class LdapAuthService
{
    private readonly IConfiguration _configuration;
    private readonly ILogger<LdapAuthService> _logger;

    public LdapAuthService(IConfiguration configuration, ILogger<LdapAuthService> logger)
    {
        _configuration = configuration;
        _logger = logger;
    }

    public virtual async Task<LdapAuthResult> AuthenticateAsync(string username, string password)
    {
        var host = _configuration["Ldap:Host"]!;
        var port = _configuration.GetValue("Ldap:Port", 389);
        var useSsl = _configuration.GetValue("Ldap:UseSsl", false);
        var baseDn = _configuration["Ldap:BaseDn"]!;
        var searchFilter = _configuration["Ldap:UserSearchFilter"] ?? $"(uid={username})";
        var bindDn = _configuration["Ldap:BindDn"];
        var bindPassword = _configuration["Ldap:BindPassword"];
        var emailAttribute = _configuration["Ldap:EmailAttribute"] ?? "mail";
        var displayNameAttribute = _configuration["Ldap:DisplayNameAttribute"] ?? "displayName";

        var resolvedFilter = searchFilter.Replace("{0}", LdapEscapeFilter(username));

        using var connection = new LdapConnection();

        try
        {
            if (useSsl)
                connection.SecureSocketLayer = true;

            await Task.Run(() => connection.Connect(host, port));

            if (!string.IsNullOrEmpty(bindDn))
            {
                await Task.Run(() => connection.Bind(bindDn, bindPassword));
            }

            var searchResults = await Task.Run(() => connection.Search(
                baseDn,
                LdapConnection.ScopeSub,
                resolvedFilter,
                new[] { emailAttribute, displayNameAttribute, "dn" },
                false));

            LdapEntry? entry = null;
            await Task.Run(() =>
            {
                if (searchResults.HasMore())
                    entry = searchResults.Next();
            });

            if (entry is null)
            {
                _logger.LogWarning("LDAP user not found: Username={Username}, Host={Host}", username, host);
                return new LdapAuthResult(false, null, null, null);
            }

            var userDn = entry.Dn;

            using var userConnection = new LdapConnection();
            if (useSsl)
                userConnection.SecureSocketLayer = true;

            await Task.Run(() => userConnection.Connect(host, port));

            try
            {
                await Task.Run(() => userConnection.Bind(userDn, password));
            }
            catch (LdapException ex) when (ex.ResultCode == LdapException.InvalidCredentials)
            {
                _logger.LogWarning("LDAP bind failed: Username={Username}, Host={Host}, ErrorType={ErrorType}",
                    username, host, ex.GetType().Name);
                return new LdapAuthResult(false, null, null, null);
            }

            var email = GetAttributeValue(entry, emailAttribute);
            var displayName = GetAttributeValue(entry, displayNameAttribute);

            _logger.LogInformation("LDAP login success: Username={Username}, Email={Email}, Host={Host}",
                username, email, host);

            return new LdapAuthResult(true, email, displayName, userDn);
        }
        catch (LdapException ex) when (ex.ResultCode == LdapException.InvalidCredentials)
        {
            _logger.LogWarning("LDAP bind failed: Username={Username}, Host={Host}, ErrorType={ErrorType}",
                username, host, ex.GetType().Name);
            return new LdapAuthResult(false, null, null, null);
        }
        catch (LdapException ex)
        {
            _logger.LogError(ex, "LDAP connection failure: Host={Host}, Port={Port}", host, port);
            throw;
        }
    }

    private static string? GetAttributeValue(LdapEntry entry, string attributeName)
    {
        try
        {
            return entry.GetAttribute(attributeName)?.StringValue;
        }
        catch (KeyNotFoundException)
        {
            return null;
        }
    }

    private static string LdapEscapeFilter(string value)
    {
        return value
            .Replace("\\", "\\5c")
            .Replace("*", "\\2a")
            .Replace("(", "\\28")
            .Replace(")", "\\29")
            .Replace("\0", "\\00");
    }
}
