import re

file_path = r'AudioEffector\Views\SidebarControl.xaml'
with open(file_path, 'r', encoding='utf-8') as f:
    content = f.read()

# Make sure we import the viewmodels namespace at the top if it's missing
if 'xmlns:vm=' not in content:
    content = content.replace('xmlns:local="clr-namespace:AudioEffector.Views"', 'xmlns:local="clr-namespace:AudioEffector.Views"\n             xmlns:vm="clr-namespace:AudioEffector.ViewModels"')

# We will replace individual RadioButton commands with SwitchViewCommand
# 1. All Songs
content = content.replace('<!-- All Songs (Music Note) -->\n                <RadioButton Style="{StaticResource SidebarRadioButtonStyle}">', '<!-- All Songs (Music Note) -->\n                <RadioButton Style="{StaticResource SidebarRadioButtonStyle}" Command="{Binding SwitchViewCommand}" CommandParameter="{x:Static vm:ViewType.AllSongs}">')
# 2. Albums
content = content.replace('Command="{Binding ShowLibraryCommand}"', 'Command="{Binding SwitchViewCommand}" CommandParameter="{x:Static vm:ViewType.Albums}"')
# 3. Artists
content = content.replace('<!-- Artists (Microphone) -->\n                <RadioButton Style="{StaticResource SidebarRadioButtonStyle}">', '<!-- Artists (Microphone) -->\n                <RadioButton Style="{StaticResource SidebarRadioButtonStyle}" Command="{Binding SwitchViewCommand}" CommandParameter="{x:Static vm:ViewType.Artists}">')
# 4. Folders
content = content.replace('Command="{Binding ShowFolderCommand}"', 'Command="{Binding SwitchViewCommand}" CommandParameter="{x:Static vm:ViewType.Folders}"')
# 5. Favorites
content = content.replace('Command="{Binding ShowFavoritesCommand}"', 'Command="{Binding SwitchViewCommand}" CommandParameter="{x:Static vm:ViewType.Favorites}"')
# 6. Playlists
content = content.replace('Command="{Binding ShowPlaylistSelectorCommand}"', 'Command="{Binding SwitchViewCommand}" CommandParameter="{x:Static vm:ViewType.Playlists}"')
# 7. Recent
content = content.replace('<!-- Recent -->\n                <RadioButton Style="{StaticResource SidebarRadioButtonStyle}">', '<!-- Recent -->\n                <RadioButton Style="{StaticResource SidebarRadioButtonStyle}" Command="{Binding SwitchViewCommand}" CommandParameter="{x:Static vm:ViewType.Recent}">')

with open(file_path, 'w', encoding='utf-8') as f:
    f.write(content)
print("Updated SidebarControl.xaml")
