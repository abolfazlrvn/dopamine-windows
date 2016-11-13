﻿using Digimezzo.WPFControls.Enums;
using Dopamine.Common.Services.I18n;
using Dopamine.Common.Services.Playback;
using Dopamine.Core.Api.Lastfm;
using Dopamine.Core.Base;
using Dopamine.Core.Database;
using Dopamine.Core.Database.Repositories.Interfaces;
using Dopamine.Core.Logging;
using Dopamine.Core.Settings;
using Dopamine.Core.Utils;
using Microsoft.Practices.Unity;
using Prism.Mvvm;
using System;
using System.Threading.Tasks;

namespace Dopamine.Common.Presentation.ViewModels
{
    public class ArtistInfoControlViewModel : BindableBase
    {
        #region Variables
        private IUnityContainer container;
        private ArtistInfoViewModel artistInfoViewModel;
        private IPlaybackService playbackService;
        private II18nService i18nService;
        private ITrackRepository trackRepository;
        private Core.Database.Entities.Artist previousArtist;
        private Core.Database.Entities.Artist artist;
        private SlideDirection slideDirection;
        private bool isBusy;
        #endregion

        #region Properties
        public SlideDirection SlideDirection
        {
            get { return this.slideDirection; }
            set { SetProperty<SlideDirection>(ref this.slideDirection, value); }
        }

        public ArtistInfoViewModel ArtistInfoViewModel
        {
            get { return this.artistInfoViewModel; }
            set { SetProperty<ArtistInfoViewModel>(ref this.artistInfoViewModel, value); }
        }

        public bool IsBusy
        {
            get { return this.isBusy; }
            set { SetProperty<bool>(ref this.isBusy, value); }
        }

        #endregion

        #region Construction
        public ArtistInfoControlViewModel(IUnityContainer container, IPlaybackService playbackService, II18nService i18nService, ITrackRepository trackRepository)
        {
            this.container = container;
            this.playbackService = playbackService;
            this.i18nService = i18nService;
            this.trackRepository = trackRepository;

            this.SlideDirection = SlideDirection.LeftToRight; // Default SlideDirection

            this.playbackService.PlaybackSuccess += async (isPlayingPreviousTrack) =>
            {
                this.SlideDirection = SlideDirection.LeftToRight;
                if (isPlayingPreviousTrack) this.SlideDirection = SlideDirection.RightToLeft;
                await this.ShowArtistInfoAsync(this.playbackService.PlayingTrack, false);
                this.SlideDirection = SlideDirection.LeftToRight;
            };

            this.i18nService.LanguageChanged += async (_, __) =>
            {
                if (this.playbackService.PlayingTrack != null) await this.ShowArtistInfoAsync(this.playbackService.PlayingTrack, true);
            };

            this.ShowArtistInfoAsync(this.playbackService.PlayingTrack, true);
        }
        #endregion

        #region Private
        private async Task ShowArtistInfoAsync(string trackInfo, bool forceReload)
        {
            this.previousArtist = this.artist;

            // User doesn't want to download artist info, or no track is selected.
            if (!XmlSettingsClient.Instance.Get<bool>("Lastfm", "DownloadArtistInformation") || trackInfo == null)
            {
                this.ArtistInfoViewModel = this.container.Resolve<ArtistInfoViewModel>();
                this.artist = null;
                return;
            }

            // Get the track from the database
            var dbTrack = await this.trackRepository.GetTrackInfoAsync(trackInfo);

            if (dbTrack == null)
            {
                LogClient.Instance.Logger.Error("Track not found in the database: {0}", trackInfo);

                this.ArtistInfoViewModel = this.container.Resolve<ArtistInfoViewModel>();
                this.artist = null;
                return;
            }

            // Artist name is unknown
            if (dbTrack.ArtistName == Defaults.UnknownArtistString)
            {
                ArtistInfoViewModel localArtistInfoViewModel = this.container.Resolve<ArtistInfoViewModel>();
                await localArtistInfoViewModel.SetLastFmArtistAsync(new Core.Api.Lastfm.Artist { Name = Defaults.UnknownArtistString });
                this.ArtistInfoViewModel = localArtistInfoViewModel;
                this.artist = null;
                return;
            }

            this.artist = new Core.Database.Entities.Artist
            {
                ArtistName = dbTrack.ArtistName
            };

            // The artist didn't change: leave the previous artist info.
            if (this.artist.Equals(this.previousArtist) & !forceReload) return;

            // The artist changed: we need to show new artist info.
            string artworkPath = string.Empty;

            this.IsBusy = true;

            try
            {
                Core.Api.Lastfm.Artist lfmArtist = await LastfmApi.ArtistGetInfo(dbTrack.ArtistName, true, ResourceUtils.GetStringResource("Language_ISO639-1"));

                if (lfmArtist != null)
                {
                    if (string.IsNullOrEmpty(lfmArtist.Biography.Content))
                    {
                        // In case there is no localized Biography, get the English one.
                        lfmArtist = await LastfmApi.ArtistGetInfo(dbTrack.ArtistName, true, "EN");
                    }

                    if (lfmArtist != null)
                    {
                        ArtistInfoViewModel localArtistInfoViewModel = this.container.Resolve<ArtistInfoViewModel>();
                        await localArtistInfoViewModel.SetLastFmArtistAsync(lfmArtist);
                        this.ArtistInfoViewModel = localArtistInfoViewModel;
                    }
                    else
                    {
                        throw new Exception("lfmArtist == null");
                    }
                }
            }
            catch (Exception ex)
            {
                LogClient.Instance.Logger.Error("Could not show artist information for Track {0}. Exception: {1}", trackInfo, ex.Message);
                this.ArtistInfoViewModel = this.container.Resolve<ArtistInfoViewModel>();
                this.artist = null;
            }

            this.IsBusy = false;
        }
        #endregion
    }
}
