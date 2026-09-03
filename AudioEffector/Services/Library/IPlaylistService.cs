using AudioEffector.Domain.Entities;
using AudioEffector.Infrastructure.Logging;
using System.Collections.Generic;

namespace AudioEffector.Services
{
    public interface IPlaylistService
    {
        [LogDescription("ユーザープレイリストを読み込みます")]
        List<UserPlaylist> LoadPlaylists();

        [LogDescription("ユーザープレイリストを保存します")]
        void SavePlaylists(List<UserPlaylist> playlists);
    }
}
