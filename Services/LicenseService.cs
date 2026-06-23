using System.Net.Http;
using Microsoft.Win32;

namespace EkipppOptimizer.Services;

public enum LicenseStatus { Ok, InvalidKey, Revoked, Expired, MaxMachines, NetworkError }

public record LicenseValidation(bool Success, LicenseStatus Status);

public class LicenseService
{
    public const string SupabaseUrl = "https://yrgpndfperwazvrtpgyj.supabase.co";
    public const string AnonKey     = "sb_publishable_ojOVzqSlYONkgrUkWp4uPw_3Yqp-Naz";
    private const string RegPath    = @"SOFTWARE\EKIPPP-OPTIMIZER\License";

    // Format attendu : XXXX-XXXX-XXXX-XXXX (lettres majuscules + chiffres)
    private static bool IsValidKeyFormat(string key) =>
        System.Text.RegularExpressions.Regex.IsMatch(
            key, @"^[A-Z0-9]{4}-[A-Z0-9]{4}-[A-Z0-9]{4}-[A-Z0-9]{4}$");

    // XOR la clé avec le MachineId — liée à la machine, illisible en regedit
    private static string ObfuscateKey(string key)
    {
        var keyBytes  = System.Text.Encoding.UTF8.GetBytes(key);
        var machineId = GetMachineId();
        var salt      = System.Text.Encoding.UTF8.GetBytes(
                            (machineId + machineId).PadRight(64)[..Math.Min(64, (machineId + machineId).Length)]);
        var result = new byte[keyBytes.Length];
        for (int i = 0; i < keyBytes.Length; i++)
            result[i] = (byte)(keyBytes[i] ^ salt[i % salt.Length]);
        return Convert.ToBase64String(result);
    }

    private static string? DeobfuscateKey(string stored)
    {
        try
        {
            var encrypted = Convert.FromBase64String(stored);
            var machineId = GetMachineId();
            var salt      = System.Text.Encoding.UTF8.GetBytes(
                                (machineId + machineId).PadRight(64)[..Math.Min(64, (machineId + machineId).Length)]);
            var result = new byte[encrypted.Length];
            for (int i = 0; i < encrypted.Length; i++)
                result[i] = (byte)(encrypted[i] ^ salt[i % salt.Length]);
            var key = System.Text.Encoding.UTF8.GetString(result);
            return IsValidKeyFormat(key) ? key : null;
        }
        catch { return null; }
    }

    public bool IsActivatedLocally() => LoadKey() != null;

    public async Task<LicenseValidation> ActivateAsync(string rawKey)
    {
        var key = rawKey.Trim().ToUpperInvariant();
        if (!IsValidKeyFormat(key))
            return new LicenseValidation(false, LicenseStatus.InvalidKey);

        try
        {
            using var http = CreateClient();
            var payload = System.Text.Json.JsonSerializer.Serialize(
                new { p_key = key, p_machine_id = GetMachineId() });
            var resp = await http.PostAsync(
                $"{SupabaseUrl}/rest/v1/rpc/validate_and_activate",
                new StringContent(payload, System.Text.Encoding.UTF8, "application/json"));

            var json = await resp.Content.ReadAsStringAsync();
            using var doc = System.Text.Json.JsonDocument.Parse(json);
            var root = doc.RootElement;

            if (root.TryGetProperty("success", out var s) && s.GetBoolean())
            {
                SaveKey(key);
                return new LicenseValidation(true, LicenseStatus.Ok);
            }

            var err = root.TryGetProperty("error", out var e) ? e.GetString() ?? "" : "";
            var status = err switch
            {
                "REVOKED"      => LicenseStatus.Revoked,
                "EXPIRED"      => LicenseStatus.Expired,
                "MAX_MACHINES" => LicenseStatus.MaxMachines,
                _              => LicenseStatus.InvalidKey,
            };
            return new LicenseValidation(false, status);
        }
        catch
        {
            return new LicenseValidation(false, LicenseStatus.NetworkError);
        }
    }

    public async Task<bool> ValidateStoredAsync()
    {
        var key = LoadKey();
        if (key == null) return false;
        var result = await ActivateAsync(key);
        if (result.Status is LicenseStatus.Revoked or LicenseStatus.Expired)
        {
            ClearKey();
            return false;
        }
        // NetworkError accepté : on ne punit pas un utilisateur légitime sans internet
        return result.Success || result.Status == LicenseStatus.NetworkError;
    }

    public void ClearKey()
    {
        try { Registry.CurrentUser.DeleteSubKey(RegPath, false); } catch { }
    }

    private void SaveKey(string key)
    {
        try
        {
            using var k = Registry.CurrentUser.CreateSubKey(RegPath);
            k.SetValue("Key", ObfuscateKey(key));
        }
        catch { }
    }

    private string? LoadKey()
    {
        try
        {
            using var k = Registry.CurrentUser.OpenSubKey(RegPath);
            var stored = k?.GetValue("Key")?.ToString();
            if (string.IsNullOrEmpty(stored)) return null;
            return DeobfuscateKey(stored);
        }
        catch { return null; }
    }

    public static string GetMachineId()
    {
        try
        {
            using var k = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Cryptography");
            var guid = k?.GetValue("MachineGuid")?.ToString();
            if (!string.IsNullOrEmpty(guid)) return guid;
        }
        catch { }
        return Environment.MachineName;
    }

    private static HttpClient CreateClient()
    {
        var http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
        http.DefaultRequestHeaders.Add("apikey", AnonKey);
        http.DefaultRequestHeaders.Add("Authorization", $"Bearer {AnonKey}");
        return http;
    }
}
