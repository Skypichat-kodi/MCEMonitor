using System;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;

namespace MediaMonitor.UI
{
    public partial class InfoPopupWindow : Window
    {
        private readonly MediaUsageItem _info;

        public InfoPopupWindow(MediaUsageItem info)
        {
            InitializeComponent();
            _info = info;

            Loaded += InfoPopupWindow_Loaded;
            ContentRendered += InfoPopupWindow_ContentRendered; // <-- Ajout essentiel
        }

        private void InfoPopupWindow_Loaded(object sender, RoutedEventArgs e)
        {
            LoadInfo();
            // SafeStartTitleScroll();  <-- Supprimé ici
        }

        private void InfoPopupWindow_ContentRendered(object sender, EventArgs e)
        {
            SafeStartTitleScroll(); // <-- Défilement après rendu complet
        }

        private static readonly BitmapImage DefaultCover =
            new(new Uri("pack://application:,,,/Resources/Images/default-cover.png"));

        private void LoadInfo()
        {
            // === TITRE ===
            if (_info.MediaType == "Video" && !string.IsNullOrEmpty(_info.EpisodeName))
            {
                TitleText.Text = _info.EpisodeName;
            }
            else if (_info.MediaType == "Audio")
            {
                TitleText.Text = string.IsNullOrWhiteSpace(_info.Title)
                    ? _info.FileName
                    : _info.Title;
            }
            else
            {
                TitleText.Text = _info.FileName;
            }

            // === CHEMIN & TYPE ===
            PathText.Text = _info.Path;
            TypeText.Text = _info.MediaType;

            // === TAILLE ===
            try
            {
                var fi = new FileInfo(_info.Path);
                SizeText.Text = $"{fi.Length / 1024 / 1024.0:F2} Mo";
            }
            catch
            {
                SizeText.Text = "—";
            }

            // === ICÔNE ===
            try
            {
                IconType.Source = new BitmapImage(new Uri(_info.IconPath));
            }
            catch
            {
                IconType.Source = null;
            }

            // === DURÉE ===
            if (_info.Duration > 0)
            {
                DurationLabel.Visibility = Visibility.Visible;
                DurationText.Visibility = Visibility.Visible;
                DurationText.Text = TimeSpan.FromSeconds(_info.Duration).ToString(@"mm\:ss");
            }
            else
            {
                DurationLabel.Visibility = Visibility.Collapsed;
                DurationText.Visibility = Visibility.Collapsed;
            }

            // === VIDÉO ===
            if (_info.MediaType == "Video")
            {
                if (!string.IsNullOrEmpty(_info.SeriesName))
                {
                    SeriesLabel.Visibility = Visibility.Visible;
                    SeriesText.Visibility = Visibility.Visible;
                    SeriesText.Text = _info.SeriesName;
                }

                if (_info.Saison > 0 || _info.Episode > 0)
                {
                    SeasonEpisodeLabel.Visibility = Visibility.Visible;
                    SeasonEpisodeText.Visibility = Visibility.Visible;
                    SeasonEpisodeText.Text = $"{_info.Saison:00}x{_info.Episode:00}";
                }

                if (!string.IsNullOrEmpty(_info.EpisodeName))
                {
                    EpisodeLabel.Visibility = Visibility.Visible;
                    EpisodeText.Visibility = Visibility.Visible;
                    EpisodeText.Text = _info.EpisodeName;
                }

                if (!string.IsNullOrEmpty(_info.VideoCodec))
                {
                    VideoCodecLabel.Visibility = Visibility.Visible;
                    VideoCodecText.Visibility = Visibility.Visible;
                    VideoCodecText.Text = _info.VideoCodec;
                }

                if (!string.IsNullOrEmpty(_info.AudioCodec))
                {
                    AudioCodecLabel.Visibility = Visibility.Visible;
                    AudioCodecText.Visibility = Visibility.Visible;
                    AudioCodecText.Text = _info.AudioCodec;
                }

                // Masquer les tags audio
                Id3TitleLabel.Visibility = Visibility.Collapsed;
                Id3Title.Visibility = Visibility.Collapsed;
                Id3ArtistLabel.Visibility = Visibility.Collapsed;
                Id3Artist.Visibility = Visibility.Collapsed;
                Id3AlbumLabel.Visibility = Visibility.Collapsed;
                Id3Album.Visibility = Visibility.Collapsed;
                Id3YearLabel.Visibility = Visibility.Collapsed;
                Id3Year.Visibility = Visibility.Collapsed;
                Id3TrackLabel.Visibility = Visibility.Collapsed;
                Id3Track.Visibility = Visibility.Collapsed;
                Id3GenreLabel.Visibility = Visibility.Collapsed;
                Id3Genre.Visibility = Visibility.Collapsed;

                // Miniature vidéo
                AlbumArtBorder.Width = 260;
                AlbumArtBorder.Height = 146;
                AlbumArtImage.Stretch = Stretch.UniformToFill;
                VideoOverlay.Visibility = Visibility.Visible;
            }
            else
            {
                // === AUDIO ===
                Id3TitleLabel.Visibility = Visibility.Visible;
                Id3Title.Visibility = Visibility.Visible;
                Id3ArtistLabel.Visibility = Visibility.Visible;
                Id3Artist.Visibility = Visibility.Visible;
                Id3AlbumLabel.Visibility = Visibility.Visible;
                Id3Album.Visibility = Visibility.Visible;
                Id3YearLabel.Visibility = Visibility.Visible;
                Id3Year.Visibility = Visibility.Visible;
                Id3TrackLabel.Visibility = Visibility.Visible;
                Id3Track.Visibility = Visibility.Visible;
                Id3GenreLabel.Visibility = Visibility.Visible;
                Id3Genre.Visibility = Visibility.Visible;

                Id3Title.Text = string.IsNullOrEmpty(_info.Title) ? "—" : _info.Title;
                Id3Artist.Text = string.IsNullOrEmpty(_info.Artist) ? "—" : _info.Artist;
                Id3Album.Text = string.IsNullOrEmpty(_info.Album) ? "—" : _info.Album;
                Id3Year.Text = _info.Year > 0 ? _info.Year.ToString() : "—";
                Id3Track.Text = _info.Track > 0 ? _info.Track.ToString() : "—";
                Id3Genre.Text = string.IsNullOrEmpty(_info.Genre) ? "—" : _info.Genre;

                // Miniature audio
                AlbumArtBorder.Width = 120;
                AlbumArtBorder.Height = 120;
                AlbumArtImage.Stretch = Stretch.UniformToFill;
                VideoOverlay.Visibility = Visibility.Collapsed;
            }

            // === MINIATURE ===
            if (_info.AlbumArt != null && _info.AlbumArt.Length > 0)
            {
                using var ms = new MemoryStream(_info.AlbumArt);

                AlbumArtImage.Source = BitmapFrame.Create(
                    ms,
                    BitmapCreateOptions.IgnoreImageCache | BitmapCreateOptions.PreservePixelFormat,
                    BitmapCacheOption.OnLoad
                );
            }
            else
            {
                AlbumArtImage.Source = DefaultCover;
            }
        }

        // Animation vignette
        private void AlbumArtImage_Loaded(object sender, RoutedEventArgs e)
        {
            try
            {
                var anim = (Storyboard)FindResource("ThumbAppearAnimation");
                anim.Begin(AlbumArtBorder);
            }
            catch { }
        }

        // Défilement intelligent du titre
        private void SafeStartTitleScroll()
        {
            if (TitleText == null || TitleScrollContainer == null)
                return;

            TitleText.UpdateLayout();
            TitleScrollContainer.UpdateLayout();

            double textWidth = TitleText.ActualWidth;
            double containerWidth = TitleScrollContainer.ActualWidth;

            if (double.IsNaN(textWidth) || double.IsNaN(containerWidth))
                return;

            if (textWidth <= containerWidth)
            {
                TitleScrollTransform.X = 0;
                return;
            }

            double overflow = textWidth - containerWidth;
            if (overflow <= 0)
                return;

            var anim = new DoubleAnimation
            {
                From = 0,
                To = -overflow,
                Duration = TimeSpan.FromSeconds(4),
                AutoReverse = true,
                RepeatBehavior = RepeatBehavior.Forever,
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseInOut }
            };

            TitleScrollTransform.BeginAnimation(TranslateTransform.XProperty, anim);
        }
    }
}

