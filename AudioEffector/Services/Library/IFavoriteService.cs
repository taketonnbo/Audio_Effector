using System.Collections.Generic;
using AudioEffector.Infrastructure.Logging;

namespace AudioEffector.Services
{
    public interface IFavoriteService
    {
        [LogDescription("お気に入りリストを読み込みます")]
        List<string> LoadFavorites();

        [LogDescription("お気に入りリストを保存します")]
        void SaveFavorites(List<string> favorites);
    }
}
