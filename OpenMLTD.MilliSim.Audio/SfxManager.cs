using System;
using System.Collections.Generic;
using System.IO;
using JetBrains.Annotations;
using NAudio.Wave;
using OpenMLTD.MilliSim.Audio.Extending;
using OpenMLTD.MilliSim.Core;
using OpenMLTD.MilliSim.Core.Extensions;

namespace OpenMLTD.MilliSim.Audio {
    public sealed class SfxManager : DisposableBase {

        internal SfxManager(AudioManager audioManager) {
            _audioManager = audioManager;
        }

        public void PreloadSfx([CanBeNull] string fileName, [NotNull] IAudioFormat format) {
            if (fileName == null) {
                return;
            }

            fileName = Path.GetFullPath(fileName);

            if (!File.Exists(fileName)) {
                throw new FileNotFoundException("Audio file is not found.", fileName);
            }

            var key = Environment.OSVersion.Platform == PlatformID.Win32NT ? fileName.ToLowerInvariant() : fileName;

            if (_preloaded.ContainsKey(key)) {
                return;
            }

            using (var waveStream = format.Read(fileName)) {
                byte[] data;
                var buf = new byte[1024];

                using (var memoryStream = new MemoryStream()) {
                    var read = waveStream.Read(buf, 0, buf.Length);
                    while (read > 0) {
                        memoryStream.Write(buf, 0, read);

                        if (read < buf.Length) {
                            break;
                        }

                        read = waveStream.Read(buf, 0, buf.Length);
                    }
                    data = memoryStream.ToArray();
                }

                var waveFormat = waveStream.WaveFormat;
                _preloaded.Add(key, (data, waveFormat));
            }
        }

        public void DiscardAllPreloadedSfx() {
            _preloaded.Clear();
        }

        public float Volume { get; set; } = 1f;

        public void Play([CanBeNull] string fileName, [NotNull] IAudioFormat format) {
            if (fileName == null) {
                return;
            }

            fileName = Path.GetFullPath(fileName);

            PreloadSfx(fileName, format);

            var key = Environment.OSVersion.Platform == PlatformID.Win32NT ? fileName.ToLowerInvariant() : fileName;

            var currentTime = _audioManager.MixerTime;

            var free = GetFreeStream(key);
            if (free.OffsetStream != null) {
                free.OffsetStream.StartTime = currentTime;
                free.OffsetStream.CurrentTime = currentTime;
                _playingStates[free.Index] = true;
                _audioManager.AddInputStream(free.OffsetStream, Volume);
                return;
            }

            var (data, waveFormat) = _preloaded[key];

            var source = new RawSourceWaveStream(data, 0, data.Length, waveFormat);

            // Offset requires 16-bit integer input.
            WaveStream toOffset;
            if (AudioHelper.NeedsFormatConversionFrom(waveFormat, RequiredFormat)) {
                toOffset = new ResamplerDmoStream(source, RequiredFormat);
            } else {
                toOffset = source;
            }

            var offset = new WaveOffsetStream(toOffset, currentTime, TimeSpan.Zero, toOffset.TotalTime);

            _audioManager.AddInputStream(offset, Volume);

            lock (_queueLock) {
                _playingWaveStreams.Add((key, offset, toOffset, source));
            }

            _playingStates.Add(true);
        }

        public void PlayLooped([CanBeNull] string fileName, [NotNull] IAudioFormat format, [NotNull] object state) {
            if (fileName == null) {
                return;
            }

            if (state == null) {
                throw new ArgumentNullException(nameof(state));
            }

            if (_loopedStreams.ContainsKey(state)) {
                return;
            }

            fileName = Path.GetFullPath(fileName);

            PreloadSfx(fileName, format);

            var key = Environment.OSVersion.Platform == PlatformID.Win32NT ? fileName.ToLowerInvariant() : fileName;

            var currentTime = _audioManager.MixerTime;

            var (data, waveFormat) = _preloaded[key];

            var source = new RawSourceWaveStream(data, 0, data.Length, waveFormat);

            var looped = new LoopedWaveStream(source, LoopedWaveStream.DefaultMaxLoops);

            // Offset requires 16-bit integer input.
            WaveStream toOffset;
            if (AudioHelper.NeedsFormatConversionFrom(waveFormat, RequiredFormat)) {
                toOffset = new ResamplerDmoStream(looped, RequiredFormat);
            } else {
                toOffset = looped;
            }

            var offset = new WaveOffsetStream(toOffset, currentTime, TimeSpan.Zero, toOffset.TotalTime);

            _audioManager.AddInputStream(offset, Volume);

            lock (_queueLock) {
                _loopedStreams[state] = (offset, toOffset, looped, source);
            }
        }

        public void StopLooped([NotNull] object state) {
            if (state == null) {
                throw new ArgumentNullException(nameof(state));
            }

            lock (_queueLock) {
                if (!_loopedStreams.ContainsKey(state)) {
                    return;
                }

                var (offset, toOffset, looped, source) = _loopedStreams[state];

                _audioManager.RemoveInputStream(offset);
                _loopedStreams.Remove(state);

                offset.Dispose();
                if (toOffset != looped) {
                    toOffset.Dispose();
                }
                source.Dispose();
            }
        }

        public void StopAll() {
            lock (_queueLock) {
                foreach (var (_, offset, toOffset, source) in _playingWaveStreams) {
                    _audioManager.RemoveInputStream(offset);
                    offset.Dispose();
                    toOffset.Dispose();
                    if (toOffset != source) {
                        source.Dispose();
                    }
                }

                foreach (var (_, offset, toOffset, looped, source) in _loopedStreams) {
                    _audioManager.RemoveInputStream(offset);
                    offset.Dispose();
                    if (toOffset != looped) {
                        toOffset.Dispose();
                    }
                    source.Dispose();
                }

                _playingWaveStreams.Clear();
                _loopedStreams.Clear();
                _playingStates.Clear();
            }
        }

        public void UpdateWaveQueue() {
            lock (_queueLock) {
                for (var i = 0; i < _playingWaveStreams.Count; ++i) {
                    if (!_playingStates[i]) {
                        continue;
                    }

                    var (_, stream, _, _) = _playingWaveStreams[i];
                    if (stream.Position >= stream.Length) {
                        _playingStates[i] = false;
                        _audioManager.RemoveInputStream(stream);
                    }
                }
            }
        }

        private (WaveOffsetStream OffsetStream, int Index) GetFreeStream(string key) {
            if (!_preloaded.ContainsKey(key)) {
                return (null, -1);
            }

            lock (_queueLock) {
                for (var i = 0; i < _playingWaveStreams.Count; ++i) {
                    if (_playingStates[i]) {
                        continue;
                    }

                    var (k, offset, _, _) = _playingWaveStreams[i];
                    if (k == key) {
                        return (offset, i);
                    }
                }
            }

            return (null, -1);
        }

        protected override void Dispose(bool disposing) {
            StopAll();
            DiscardAllPreloadedSfx();
        }

        private static WaveFormat RequiredFormat => new WaveFormat(44100, 16, 2);

        private readonly AudioManager _audioManager;

        private readonly List<bool> _playingStates = new List<bool>();
        private readonly List<(string Key, WaveOffsetStream Offset, WaveStream ToOffset, WaveStream Source)> _playingWaveStreams = new List<(string, WaveOffsetStream, WaveStream, WaveStream)>();
        private readonly Dictionary<string, (byte[] Data, WaveFormat Format)> _preloaded = new Dictionary<string, (byte[] Data, WaveFormat Format)>();
        private readonly Dictionary<object, (WaveOffsetStream Offset, WaveStream ToOffset, WaveStream Looped, WaveStream Source)> _loopedStreams = new Dictionary<object, (WaveOffsetStream, WaveStream, WaveStream, WaveStream)>();

        private readonly object _queueLock = new object();

    }
}
