using System.Collections.Generic;
using System.Threading.Tasks;

namespace VanillaConnect.Intercom
{
    public interface IIntercomUsersClient
    {
        Task<IEnumerable<IIntercomUser>> CreateOrUpdateWithProfileUrl(string email);
        Task<IEnumerable<IIntercomUser>> View(IIntercomUser user);
        Task<IEnumerable<IIntercomUser>> GetAllUsers();
        Task<IIntercomUser> CreateOrUpdate(IIntercomUser user);
    }
}
