using System;
using System.Drawing;
using OpenMLTD.MilliSim.Core;
using OpenMLTD.MilliSim.Foundation;
using SharpDX.Direct3D;
using SharpDX.Direct3D11;
using SharpDX.DXGI;
using SharpDX.MediaFoundation;
using Device = SharpDX.Direct3D11.Device;

namespace OpenMLTD.MilliSim.Graphics {
    public abstract class StageRenderer : RendererBase {

        protected StageRenderer(VisualGame game)
            : base(game) {
        }

        internal void Draw(IVisualContainerElement root, GameTime gameTime) {
            var context = _renderContext;

            using (_sizeLock.NewReadLock()) {
                if (_isSizeChanged) {
                    root.OnLostContext(context);

                    context?.Dispose();

                    RecreateResources(root, out context);
                    _renderContext = context;
                }
            }

            context.ClearAll();

            root.Draw(gameTime, context);

            context.Present();
        }

        /// <summary>
        /// Do not use except in <see cref="VisualGame.Dispose(bool)"/>.
        /// </summary>
        internal RenderContext RenderContext => _renderContext;

        internal Device Direct3DDevice => _direct3DDevice;

        internal SharpDX.DXGI.Device DxgiDevice => _dxgiDevice;

        internal Factory DxgiFactory => _dxgiFactory;

        internal SharpDX.DirectWrite.Factory DirectWriteFactory => _directWriteFactory;

        internal SwapChain SwapChain => _swapChain;

        internal SwapChainDescription SwapChainDescription => _swapChainDescription;

        internal DXGIDeviceManager DxgiDeviceManager => _dxgiDeviceManager;

        // Called by GameBase.
        protected internal override void Initialize() {
            RecreateResources((IVisualContainerElement)Game.Root, out _renderContext);
            OnAfterInitialization();
        }

        protected override void Dispose(bool disposing) {
            if (!disposing) {
                return;
            }
            _directWriteFactory.Dispose();
            _dxgiFactory.Dispose();
            _swapChain.Dispose();
            _direct3DDevice.Dispose();
            _dxgiDeviceManager?.Dispose();
        }

        protected abstract void CreateSwapChainAndDevice(out SwapChainDescription swapChainDescription, out SwapChain swapChain, out Device device);

        protected virtual void OnAfterInitialization() {
        }

        private void RecreateResources(IVisualContainerElement root, out RenderContext context) {
            _dxgiFactory?.Dispose();
            _swapChain?.Dispose();
            _direct3DDevice?.Dispose();
            _directWriteFactory?.Dispose();

            CreateSwapChainAndDevice(out _swapChainDescription, out _swapChain, out _direct3DDevice);
            _dxgiDevice = _direct3DDevice.QueryInterface<SharpDX.DXGI.Device>();
            _dxgiFactory = _swapChain.GetParent<Factory>();

            // Video (EVR) initialization.
            var multithread = _direct3DDevice.QueryInterface<DeviceMultithread>();
            multithread.SetMultithreadProtected(true);
            if (_dxgiDeviceManager == null) {
                try {
                    _dxgiDeviceManager = new DXGIDeviceManager();
                } catch (System.EntryPointNotFoundException) {
                    // Windows 7
                }
            }
            _dxgiDeviceManager?.ResetDevice(_direct3DDevice);

            _directWriteFactory = new SharpDX.DirectWrite.Factory();

            context = new RenderContext(this, new Size(_swapChainDescription.ModeDescription.Width, _swapChainDescription.ModeDescription.Height));
            _renderContext = context;

            root.OnLayout();

            root.OnGotContext(context);

            _isSizeChanged = false;

            ++_controlResizeCounter;
            if (_controlResizeCounter == StartupControlResizeCount) {
                // WTF...
                // Look into MediaEngine and DXGIDeviceManager?
                Game.Window.Invoke(new Action(() => Game.Window.RaiseStageReady(EventArgs.Empty)));

                root.OnStageReady(context);
            }
        }

        protected bool _isSizeChanged;
        protected readonly SimpleUsingLock _sizeLock = new SimpleUsingLock();

        // For D2D interop, and video output.
        protected const DeviceCreationFlags D3DDeviceCreationFlags = DeviceCreationFlags.BgraSupport | DeviceCreationFlags.VideoSupport;

        private Device _direct3DDevice;
        private SharpDX.DXGI.Device _dxgiDevice;
        private SwapChainDescription _swapChainDescription;
        private SwapChain _swapChain;
        private Factory _dxgiFactory;
        private SharpDX.DirectWrite.Factory _directWriteFactory;
        private DXGIDeviceManager _dxgiDeviceManager;

        protected static readonly Rational DefaultRefreshRate = new Rational(60, 1);

        private RenderContext _renderContext;

        // These two counters work as follows.
        // We assume that the stage is resized only for a certain number of times. During each size change,
        // increase the counter. And finally, all resizing actions are complete and the size will never
        // change in the future.
        // In current model, the size changes 2 times. One for window creation (by WinForms), one for setting
        // ClientSize on GameWindow (by user's config entry).
        private int _controlResizeCounter;
        private static int StartupControlResizeCount = 2;

    }
}
