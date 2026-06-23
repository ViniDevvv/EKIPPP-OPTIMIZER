using System.Net.Http;
using Microsoft.Win32;

namespace EkipppOptimizer.Services;

public enum LicenseStatus { Ok, InvalidKey, Revoked, Expired, MaxMachines, NetworkError }

public record LicenseValidation(bool Success, LicenseStatus Status);

public class LicenseService
{
    // ── À remplir avec tes credentials Supabase ────────────────────────────
    public const string SupabaseUrl = "https://yrgpndfperwazvrtpgyj.supabase.co";
    public const string AnonKey     = "sb_publishable_ojOVzqSlYONkgrUkWp4uPw_3Yqp-Naz";
    // ───────────────────────────────────────────────────────────────────────

    private const string RegPath = @"SOFTWARE\EKIPPP-OPTIMIZER\License";

    public bool IsActivatedLocally()
    {
        try
        {
            using var k = Registry.CurrentUser.OpenSubKey(RegPath);
            return !string.IsNullOrWhiteSpace(k?.GetValue("Key")?.ToString());
        }
        catch { return false; }
    }

    public async Task<LicenseValidation> ActivateAsync(string rawKey)
    {
        var key = rawKey.Trim().ToUpperInvariant();
        if (string.IsNullOrEmpty(key))
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
            k.SetValue("Key", key);
        }
        catch { }
    }

    private string? LoadKey()
    {
        try
        {
            using var k = Registry.CurrentUser.OpenSubKey(RegPath);
            return k?.GetValue("Key")?.ToString();
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
