/*
 * PulseAPI JS client — the browser-side counterpart to PulseAPI.CSharp.
 *
 * "The object is the wire": pulse_v1 responses are the serialized contract
 * objects verbatim. Field names are PascalCase (Name, CoverArt, Kind, ...) and
 * are NOT transformed -- read them exactly as the C# types declare them.
 *
 * Every read endpoint returns a PulseResponse envelope:
 *   { status, version, contentType, contents }
 * where contentType is "PulseObject" (contents is one object) or
 * "PulseObjectList" (contents is an array). Failures encode a code in `status`
 * ("not_found", "missing_id", ...) -- there is no message field. This client
 * surfaces a non-"ok" status by rejecting with a PulseApiError carrying the
 * status code.
 *
 * Loads as a global (window.PulseAPI) for <script src> use, or via
 * module.exports under CommonJS.
 */
(function (root, factory) {
	if (typeof module === 'object' && module.exports) {
		module.exports = factory();
	} else {
		root.PulseAPI = factory();
	}
}(typeof self !== 'undefined' ? self : this, function () {
	'use strict';

	// 1:1 with PulseAPI.CSharp.eDataType. JsonStringEnumConverter emits the
	// member name verbatim, so each Pulse* object's `Kind` field is one of these
	// strings. Branch on Kind to tell heterogeneous list items apart.
	var DataType = {
		Track: 'Track',
		Album: 'Album',
		AlbumTracks: 'AlbumTracks',
		Playlist: 'Playlist',
		PlaylistTracks: 'PlaylistTracks',
		Artist: 'Artist',
		ArtistAlbums: 'ArtistAlbums',
		ArtistTracks: 'ArtistTracks',
		Genre: 'Genre',
		GenreDetails: 'GenreDetails',
		Podcast: 'Podcast',
		PodcastDetails: 'PodcastDetails',
		PodcastEpisode: 'PodcastEpisode',
		Audiobook: 'Audiobook',
		Chapter: 'Chapter',
		AudiobookDetails: 'AudiobookDetails',
		CoverArt: 'CoverArt',
		SongData: 'SongData'
	};

	// 1:1 with PulseResponse.ContentType.
	var ContentType = {
		PulseObject: 'PulseObject',
		PulseObjectList: 'PulseObjectList'
	};

	// Rejection carried when the envelope status is not "ok", or on a transport
	// failure. `status` is the server's status code (or "http_<code>"); inspect
	// it to branch without parsing contents.
	function PulseApiError(status, endpoint) {
		this.name = 'PulseApiError';
		this.status = status;
		this.endpoint = endpoint;
		this.message = 'pulse_v1/' + endpoint + ' -> ' + status;
	}
	PulseApiError.prototype = Object.create(Error.prototype);
	PulseApiError.prototype.constructor = PulseApiError;

	// options:
	//   baseUrl   - endpoint root (default "/pulse_v1/")
	//   user      - sent as `u` on every request (legacy username)
	//   userId    - sent as `uid` on every request (stable user id)
	//   client    - sent as `c` (default "PulseWeb")
	//   version   - sent as `v` when set
	//   defaultParams - extra params merged into every request
	function PulseClient(options) {
		options = options || {};
		this.baseUrl = options.baseUrl || '/pulse_v1/';
		if (this.baseUrl.charAt(this.baseUrl.length - 1) !== '/') {
			this.baseUrl = this.baseUrl + '/';
		}
		this.user = options.user || '';
		this.userId = options.userId || '';
		this.client = options.client || 'PulseWeb';
		this.version = options.version || '';
		this.defaultParams = options.defaultParams || {};
	}

	// Merge defaults + identity params + per-call params into a query string.
	// Array values become repeated keys (key=a&key=b), matching how the server
	// reads multi-valued params like songId / songIdToAdd. null/undefined values
	// are dropped.
	PulseClient.prototype._buildQuery = function (params) {
		var merged = {};
		var key;
		for (key in this.defaultParams) {
			if (this.defaultParams.hasOwnProperty(key)) {
				merged[key] = this.defaultParams[key];
			}
		}
		if (this.user) { merged.u = this.user; }
		if (this.userId) { merged.uid = this.userId; }
		if (this.client) { merged.c = this.client; }
		if (this.version) { merged.v = this.version; }
		if (params) {
			for (key in params) {
				if (params.hasOwnProperty(key)) {
					merged[key] = params[key];
				}
			}
		}

		var parts = [];
		for (key in merged) {
			if (!merged.hasOwnProperty(key)) { continue; }
			var value = merged[key];
			if (value === null || value === undefined) { continue; }
			if (Object.prototype.toString.call(value) === '[object Array]') {
				for (var idx = 0; idx < value.length; idx++) {
					parts.push(encodeURIComponent(key) + '=' + encodeURIComponent(value[idx]));
				}
			} else {
				parts.push(encodeURIComponent(key) + '=' + encodeURIComponent(value));
			}
		}
		return parts.join('&');
	};

	// Build a request URL without fetching -- for <img>/<audio> src on stream,
	// download, and coverArt.
	PulseClient.prototype.url = function (endpoint, params) {
		var queryString = this._buildQuery(params);
		var built = this.baseUrl + endpoint;
		if (queryString) {
			built = built + '?' + queryString;
		}
		return built;
	};

	// Fetch and return the full PulseResponse envelope. Rejects with a
	// PulseApiError on transport failure or a non-"ok" status.
	PulseClient.prototype.request = function (endpoint, params) {
		var url = this.url(endpoint, params);
		return fetch(url).then(function (response) {
			if (!response.ok) {
				throw new PulseApiError('http_' + response.status, endpoint);
			}
			return response.json();
		}).then(function (data) {
			if (!data || data.status !== 'ok') {
				throw new PulseApiError((data && data.status) || 'unknown', endpoint);
			}
			return data;
		});
	};

	// Fetch and resolve to just the envelope's `contents`.
	PulseClient.prototype._contents = function (endpoint, params) {
		return this.request(endpoint, params).then(function (envelope) {
			return envelope.contents;
		});
	};

	// Report a single non-batched analytics event (play/stop). uid rides along
	// via _buildQuery. Fire-and-forget at the call site.
	PulseClient.prototype.recordAnalytic = function (action, type, id, ms) {
		var params = { action: action, type: type, id: id };
		if (ms !== null && ms !== undefined) { params.ms = ms; }
		return this.request('recordAnalytic', params);
	};

	// -- non-fetching URL helpers -----------------------------------------

	PulseClient.prototype.streamUrl = function (id) {
		return this.url('stream', { id: id });
	};

	PulseClient.prototype.downloadUrl = function (id) {
		return this.url('download', { id: id });
	};

	PulseClient.prototype.coverArtUrl = function (id, size) {
		if (!id) { return ''; }
		// Podcast discovery hits carry a remote artwork URL in place of a server
		// cover id (see searchPodcasts); load those directly.
		if (id.indexOf('http://') === 0 || id.indexOf('https://') === 0) { return id; }
		return this.url('coverArt', { id: id, size: size });
	};

	// -- system -----------------------------------------------------------

	// Resolves to the full envelope (use .version for the server version).
	PulseClient.prototype.ping = function () {
		return this.request('ping');
	};

	// -- library reads ----------------------------------------------------

	PulseClient.prototype.artists = function () {
		return this._contents('artists');
	};

	PulseClient.prototype.artist = function (id) {
		return this._contents('artist', { id: id });
	};

	PulseClient.prototype.artistTracks = function (id) {
		return this._contents('artistTracks', { id: id });
	};

	// options: { type, size, offset, fromYear, toYear, genre }
	// type: random | newest | alphabeticalbyname | alphabeticalbyartist |
	//       frequent | recent | byyear | bygenre | starred | highest
	PulseClient.prototype.albums = function (options) {
		return this._contents('albums', options || {});
	};

	PulseClient.prototype.album = function (id) {
		return this._contents('album', { id: id });
	};

	PulseClient.prototype.track = function (id) {
		return this._contents('track', { id: id });
	};

	PulseClient.prototype.genres = function () {
		return this._contents('genres');
	};

	// options: { count, offset }
	PulseClient.prototype.genreTracks = function (genre, options) {
		var params = { genre: genre };
		if (options) {
			if (options.count !== undefined) { params.count = options.count; }
			if (options.offset !== undefined) { params.offset = options.offset; }
		}
		return this._contents('genreTracks', params);
	};

	// options: { artistCount, albumCount, songCount }
	PulseClient.prototype.search = function (query, options) {
		var params = { query: query };
		if (options) {
			if (options.artistCount !== undefined) { params.artistCount = options.artistCount; }
			if (options.albumCount !== undefined) { params.albumCount = options.albumCount; }
			if (options.songCount !== undefined) { params.songCount = options.songCount; }
		}
		return this._contents('search', params);
	};

	// Mixed-kind recents shelf. options: { count, types } where types is an
	// array or CSV string of "track","artist","album","playlist" (omit for all).
	// Resolves to an array of Pulse* objects; branch on each item's Kind.
	PulseClient.prototype.recentlyPlayed = function (options) {
		options = options || {};
		var params = {};
		if (options.count !== undefined) { params.count = options.count; }
		if (options.types !== undefined && options.types !== null) {
			if (Object.prototype.toString.call(options.types) === '[object Array]') {
				params.types = options.types.join(',');
			} else {
				params.types = options.types;
			}
		}
		return this._contents('recentlyPlayed', params);
	};

	// Items ranked by Score. options: { count, types } where types is an array
	// or CSV of "track","artist","album","playlist" (omit for all). Resolves to
	// an array of Pulse* objects; branch on each item's Kind.
	PulseClient.prototype.topItems = function (options) {
		options = options || {};
		var params = {};
		if (options.count !== undefined) { params.count = options.count; }
		if (options.types !== undefined && options.types !== null) {
			if (Object.prototype.toString.call(options.types) === '[object Array]') {
				params.types = options.types.join(',');
			} else {
				params.types = options.types;
			}
		}
		return this._contents('topItems', params);
	};

	// -- playlists --------------------------------------------------------

	PulseClient.prototype.playlists = function () {
		return this._contents('playlists');
	};

	PulseClient.prototype.playlist = function (id) {
		return this._contents('playlist', { id: id });
	};

	// options: { name, songIds (array), playlistId (to overwrite existing) }
	PulseClient.prototype.createPlaylist = function (options) {
		options = options || {};
		var params = {};
		if (options.name !== undefined) { params.name = options.name; }
		if (options.playlistId !== undefined) { params.playlistId = options.playlistId; }
		if (options.songIds !== undefined) { params.songId = options.songIds; }
		return this._contents('createPlaylist', params);
	};

	// options: { playlistId, name, comment, songIdsToAdd (array),
	//            songIndexesToRemove (array) }
	PulseClient.prototype.updatePlaylist = function (options) {
		options = options || {};
		var params = { playlistId: options.playlistId };
		if (options.name !== undefined) { params.name = options.name; }
		if (options.comment !== undefined) { params.comment = options.comment; }
		if (options.songIdsToAdd !== undefined) { params.songIdToAdd = options.songIdsToAdd; }
		if (options.songIndexesToRemove !== undefined) { params.songIndexToRemove = options.songIndexesToRemove; }
		return this._contents('updatePlaylist', params);
	};

	PulseClient.prototype.deletePlaylist = function (id) {
		return this.request('deletePlaylist', { id: id });
	};

	// -- favorites / analytics --------------------------------------------

	PulseClient.prototype.favorites = function () {
		return this._contents('favorites');
	};

	// type: "track" (default) | "album" | "artist"
	PulseClient.prototype.favorite = function (id, type) {
		return this.request('favorite', { id: id, type: type });
	};

	PulseClient.prototype.unfavorite = function (id, type) {
		return this.request('unfavorite', { id: id, type: type });
	};

	// -- podcasts ---------------------------------------------------------

	// The podcasts this user is subscribed to. Resolves to an array of
	// PulsePodcast objects.
	PulseClient.prototype.podcasts = function () {
		return this._contents('podcasts');
	};

	// Discover podcasts by name through the server's configured search provider.
	// Resolves to an array of PulsePodcast hits: each has Title/Author/FeedUrl
	// and a remote artwork URL in CoverArt, but no Id (not catalogued yet). Add
	// one via addPodcast(feedUrl, true).
	PulseClient.prototype.searchPodcasts = function (query) {
		return this._contents('searchPodcasts', { query: query });
	};

	// The full podcast catalogue on the server, regardless of subscription.
	PulseClient.prototype.allPodcasts = function () {
		return this._contents('allPodcasts');
	};

	// One podcast's detail: resolves to a PulsePodcastDetails
	// ({ Series: PulsePodcast, Episodes: [PulsePodcastEpisode] }). Episodes
	// are the ones available to play (downloaded), newest first.
	PulseClient.prototype.podcast = function (id) {
		return this._contents('podcast', { id: id });
	};

	// All audiobooks scanned from the server's AudiobooksPath. Resolves to an
	// array of PulseAudiobook objects.
	PulseClient.prototype.audiobooks = function () {
		return this._contents('audiobooks');
	};

	// One audiobook's detail: resolves to a PulseAudiobookDetails
	// ({ Book: PulseAudiobook, Chapters: [PulseChapter] }), chapters in order.
	PulseClient.prototype.audiobook = function (id) {
		return this._contents('audiobook', { id: id });
	};

	// Add a feed to the catalogue by RSS URL. subscribe=true also adds it to
	// this user's subscriptions. Resolves to the PulsePodcast.
	PulseClient.prototype.addPodcast = function (feedUrl, subscribe) {
		var subscribeFlag = '0';
		if (subscribe) { subscribeFlag = '1'; }
		return this._contents('addPodcast', { feedUrl: feedUrl, subscribe: subscribeFlag });
	};

	PulseClient.prototype.subscribePodcast = function (id) {
		return this.request('subscribePodcast', { id: id });
	};

	PulseClient.prototype.unsubscribePodcast = function (id) {
		return this.request('unsubscribePodcast', { id: id });
	};

	// Change a podcast's backlog settings. settings may include retentionPolicy
	// ('KeepAll'|'KeepN'|'KeepDays'), retentionValue (int), autoDownload (bool),
	// and pollIntervalMinutes (int). Resolves to the updated PulsePodcast.
	PulseClient.prototype.updatePodcast = function (id, settings) {
		var options = settings || {};
		var params = { id: id };
		if (options.retentionPolicy !== undefined) { params.retentionPolicy = options.retentionPolicy; }
		if (options.retentionValue !== undefined) { params.retentionValue = options.retentionValue; }
		if (options.pollIntervalMinutes !== undefined) { params.pollIntervalMinutes = options.pollIntervalMinutes; }
		if (options.autoDownload !== undefined) {
			if (options.autoDownload) { params.autoDownload = '1'; } else { params.autoDownload = '0'; }
		}
		return this._contents('updatePodcast', params);
	};

	// Save this user's playback position (seconds) for an episode/chapter.
	PulseClient.prototype.saveEpisodeProgress = function (id, positionSeconds) {
		return this.request('episodeProgress', { id: id, positionSeconds: positionSeconds });
	};

	return {
		PulseClient: PulseClient,
		DataType: DataType,
		ContentType: ContentType,
		PulseApiError: PulseApiError,
		create: function (options) {
			return new PulseClient(options);
		}
	};
}));
