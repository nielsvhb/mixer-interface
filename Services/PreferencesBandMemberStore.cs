using MixerInterface.Interfaces;
using MixerInterface.Models;
using System.Text.Json;
using Microsoft.Maui.Storage;

namespace MixerInterface.Services;

public class PreferencesBandMemberStore : IBandMemberStore
{
    private const string StorageKey = "BandMembers";

    public async Task<List<BandMember>> GetAllAsync()
    {
        var json = Preferences.Get(StorageKey, string.Empty);
        if (string.IsNullOrWhiteSpace(json))
            return await Task.FromResult(new List<BandMember>());

        var members = JsonSerializer.Deserialize<List<BandMember>>(json);
        return await Task.FromResult(members ?? new List<BandMember>());
    }

    public async Task SaveAllAsync(List<BandMember> members)
    {
        var json = JsonSerializer.Serialize(members);
        Preferences.Set(StorageKey, json);
        await Task.CompletedTask;
    }
}