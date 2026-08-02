import { Composition } from "remotion";
import { KmsgVideo } from "./KmsgVideo";
import {
  CANVAS_WIDTH,
  CANVAS_HEIGHT,
  FPS,
  TOTAL_DURATION,
} from "./lib/constants";

export const RemotionRoot: React.FC = () => {
  return (
    <Composition
      id="KmsgVideo"
      component={KmsgVideo}
      durationInFrames={TOTAL_DURATION}
      fps={FPS}
      width={CANVAS_WIDTH}
      height={CANVAS_HEIGHT}
    />
  );
};
