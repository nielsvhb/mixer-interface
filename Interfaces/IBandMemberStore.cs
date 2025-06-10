using MixerInterface.Models;

namespace MixerInterface.Interfaces;

public interface IBandMemberStore
{
    Task<List<BandMember>> GetAllAsync();
    Task SaveAllAsync(List<BandMember> members); 
}