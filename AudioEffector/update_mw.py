import re

file_path = r'AudioEffector\MainWindow.xaml'
with open(file_path, 'r', encoding='utf-8') as f:
    content = f.read()

replacement = '''
            <ContentControl Grid.Row="2" Content="{Binding}">
                <ContentControl.Style>
                    <Style TargetType="ContentControl">
                        <Style.Triggers>
                            <DataTrigger Binding="{Binding CurrentViewType}" Value="{x:Static vm:ViewType.AllSongs}">
                                <Setter Property="ContentTemplate">
                                    <Setter.Value>
                                        <DataTemplate>
                                            <views:AllSongsView />
                                        </DataTemplate>
                                    </Setter.Value>
                                </Setter>
                            </DataTrigger>
                            <DataTrigger Binding="{Binding CurrentViewType}" Value="{x:Static vm:ViewType.Albums}">
                                <Setter Property="ContentTemplate">
                                    <Setter.Value>
                                        <DataTemplate>
                                            <views:LibraryView />
                                        </DataTemplate>
                                    </Setter.Value>
                                </Setter>
                            </DataTrigger>
                            <DataTrigger Binding="{Binding CurrentViewType}" Value="{x:Static vm:ViewType.Artists}">
                                <Setter Property="ContentTemplate">
                                    <Setter.Value>
                                        <DataTemplate>
                                            <views:ArtistsView />
                                        </DataTemplate>
                                    </Setter.Value>
                                </Setter>
                            </DataTrigger>
                            <DataTrigger Binding="{Binding CurrentViewType}" Value="{x:Static vm:ViewType.Folders}">
                                <Setter Property="ContentTemplate">
                                    <Setter.Value>
                                        <DataTemplate>
                                            <views:FolderView />
                                        </DataTemplate>
                                    </Setter.Value>
                                </Setter>
                            </DataTrigger>
                            <DataTrigger Binding="{Binding CurrentViewType}" Value="{x:Static vm:ViewType.Favorites}">
                                <Setter Property="ContentTemplate">
                                    <Setter.Value>
                                        <DataTemplate>
                                            <views:PlaylistTracksView />
                                        </DataTemplate>
                                    </Setter.Value>
                                </Setter>
                            </DataTrigger>
                            <DataTrigger Binding="{Binding CurrentViewType}" Value="{x:Static vm:ViewType.Playlists}">
                                <Setter Property="ContentTemplate">
                                    <Setter.Value>
                                        <DataTemplate>
                                            <views:PlaylistSelectorView />
                                        </DataTemplate>
                                    </Setter.Value>
                                </Setter>
                            </DataTrigger>
                            <DataTrigger Binding="{Binding CurrentViewType}" Value="{x:Static vm:ViewType.PlaylistTracks}">
                                <Setter Property="ContentTemplate">
                                    <Setter.Value>
                                        <DataTemplate>
                                            <views:PlaylistTracksView />
                                        </DataTemplate>
                                    </Setter.Value>
                                </Setter>
                            </DataTrigger>
                            <DataTrigger Binding="{Binding CurrentViewType}" Value="{x:Static vm:ViewType.Recent}">
                                <Setter Property="ContentTemplate">
                                    <Setter.Value>
                                        <DataTemplate>
                                            <views:RecentView />
                                        </DataTemplate>
                                    </Setter.Value>
                                </Setter>
                            </DataTrigger>
                        </Style.Triggers>
                    </Style>
                </ContentControl.Style>
            </ContentControl>
'''

pattern = r'<views:FolderView Grid\.Row="2" Visibility="\{Binding IsFolderViewVisible, Converter=\{StaticResource BoolToVis\}\}" />\s*<views:LibraryView Grid\.Row="2" Visibility="\{Binding IsLibraryVisible, Converter=\{StaticResource BoolToVis\}\}" />\s*<views:PlaylistSelectorView Grid\.Row="2" Visibility="\{Binding IsPlaylistSelectorVisible, Converter=\{StaticResource BoolToVis\}\}" />\s*<views:PlaylistTracksView Grid\.Row="2" Visibility="\{Binding IsPlaylistTracksVisible, Converter=\{StaticResource BoolToVis\}\}" />'

content = re.sub(pattern, replacement.strip(), content)

with open(file_path, 'w', encoding='utf-8') as f:
    f.write(content)
print("Updated MainWindow.xaml")
