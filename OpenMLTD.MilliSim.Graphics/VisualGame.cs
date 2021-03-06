using System.Collections.Generic;
using OpenMLTD.MilliSim.Core;
using OpenMLTD.MilliSim.Foundation;

namespace OpenMLTD.MilliSim.Graphics {
    public abstract class VisualGame : GameBase {

        public override void Draw(GameTime gameTime) {
            Stage.Draw(gameTime, StageRenderer);
        }

        public ControlStageRenderer StageRenderer => (ControlStageRenderer)BaseRenderer;

        public Stage Stage => (Stage)Root;

        protected override RendererBase CreateRenderer() {
            return new ControlStageRenderer(this, Window);
        }

        protected sealed override IContainerElement CreateRootElement(IReadOnlyList<IElement> elements) {
            return new Stage(this, elements);
        }

        protected override void Dispose(bool disposing) {
            if (disposing) {
                var renderContext = StageRenderer.RenderContext;
                (Stage as IDrawable)?.OnLostContext(renderContext);
                BaseRenderer.Dispose();
            }

            base.Dispose(disposing);
        }

    }
}
