import re

file_path = r'AudioEffector\ViewModels\MainViewModel.cs'
with open(file_path, 'r', encoding='utf-8') as f:
    content = f.read()

# 1. Add ViewType property and SwitchViewCommand
view_type_code = '''
        private ViewType _currentViewType = ViewType.Albums;
        public ViewType CurrentViewType
        {
            get => _currentViewType;
            set
            {
                if (_currentViewType != value)
                {
                    _currentViewType = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(IsLibraryVisible));
                    OnPropertyChanged(nameof(IsFolderViewVisible));
                    OnPropertyChanged(nameof(IsPlaylistSelectorVisible));
                    OnPropertyChanged(nameof(IsPlaylistTracksVisible));
                    OnPropertyChanged(nameof(IsPlaylistSectionActive));
                }
            }
        }

        public ICommand SwitchViewCommand { get; }
'''
content = content.replace('private bool _isLibraryVisible = true;', view_type_code + '\n        private bool _isLibraryVisible = true;')

# 2. Add SwitchViewCommand initialization
init_code = '''
            SwitchViewCommand = new RelayCommand(param => 
            {
                if (param is ViewType viewType)
                {
                    CurrentViewType = viewType;
                    // Handle special cases
                    if (viewType == ViewType.Favorites)
                    {
                        ShowFavorites();
                    }
                }
            });
'''
content = content.replace('ShowLibraryCommand = new RelayCommand(o => ShowLibrary());', init_code + '\n            ShowLibraryCommand = new RelayCommand(o => ShowLibrary());')

# 3. Replace boolean properties with computed ones
content = re.sub(r'public bool IsLibraryVisible\s*\{[^{}]*\}', 'public bool IsLibraryVisible => CurrentViewType == ViewType.Albums || CurrentViewType == ViewType.AllSongs || CurrentViewType == ViewType.Artists;', content)
content = re.sub(r'public bool IsFolderViewVisible\s*\{[^{}]*\}', 'public bool IsFolderViewVisible => CurrentViewType == ViewType.Folders;', content)
content = re.sub(r'public bool IsPlaylistSelectorVisible\s*\{[^{}]*\}', 'public bool IsPlaylistSelectorVisible => CurrentViewType == ViewType.Playlists;', content)
content = re.sub(r'public bool IsPlaylistTracksVisible\s*\{[^{}]*\}', 'public bool IsPlaylistTracksVisible => CurrentViewType == ViewType.PlaylistTracks || CurrentViewType == ViewType.Favorites;', content)

# 4. Remove assignments to the old backing fields in methods
def replace_assignments(text):
    text = re.sub(r'IsLibraryVisible\s*=\s*(true|false);', '', text)
    text = re.sub(r'IsFolderViewVisible\s*=\s*(true|false);', '', text)
    text = re.sub(r'IsPlaylistSelectorVisible\s*=\s*(true|false);', '', text)
    text = re.sub(r'IsPlaylistTracksVisible\s*=\s*(true|false);', '', text)
    return text

content = replace_assignments(content)

# 5. Fix specific methods to update CurrentViewType
content = content.replace('private void ShowLibrary()\n        {\n', 'private void ShowLibrary()\n        {\n            CurrentViewType = ViewType.Albums;\n')
content = content.replace('private void ShowFolder()\n        {\n', 'private void ShowFolder()\n        {\n            CurrentViewType = ViewType.Folders;\n')
content = content.replace('private void ShowPlaylistSelector()\n        {\n', 'private void ShowPlaylistSelector()\n        {\n            CurrentViewType = ViewType.Playlists;\n')
content = content.replace('System.Diagnostics.Debug.WriteLine($"ShowPlaylist: {playlist.Name}, Tracks: {playlist.TrackPaths.Count}");\n\n', 'System.Diagnostics.Debug.WriteLine($"ShowPlaylist: {playlist.Name}, Tracks: {playlist.TrackPaths.Count}");\n\n                CurrentViewType = ViewType.PlaylistTracks;\n')
content = content.replace('IsFavoritesView = true;\n', 'IsFavoritesView = true;\n            CurrentViewType = ViewType.Favorites;\n')

with open(file_path, 'w', encoding='utf-8') as f:
    f.write(content)
print("Updated MainViewModel.cs")
