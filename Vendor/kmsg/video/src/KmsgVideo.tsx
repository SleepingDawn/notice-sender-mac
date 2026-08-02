import "./index.css";
import { TransitionSeries, linearTiming } from "@remotion/transitions";
import { fade } from "@remotion/transitions/fade";
import { slide } from "@remotion/transitions/slide";
import {
  SCENE_INTRO,
  SCENE_INSTALL,
  SCENE_COMMANDS,
  SCENE_JSON,
  SCENE_REQUIREMENTS,
  SCENE_OUTRO,
  TRANSITION_DURATION,
} from "./lib/constants";
import { IntroScene } from "./scenes/IntroScene";
import { InstallScene } from "./scenes/InstallScene";
import { CommandsScene } from "./scenes/CommandsScene";
import { JsonScene } from "./scenes/JsonScene";
import { RequirementsScene } from "./scenes/RequirementsScene";
import { OutroScene } from "./scenes/OutroScene";

const transitionTiming = linearTiming({ durationInFrames: TRANSITION_DURATION });

export const KmsgVideo: React.FC = () => {
  return (
    <TransitionSeries>
      <TransitionSeries.Sequence durationInFrames={SCENE_INTRO}>
        <IntroScene />
      </TransitionSeries.Sequence>

      <TransitionSeries.Transition
        presentation={fade()}
        timing={transitionTiming}
      />

      <TransitionSeries.Sequence durationInFrames={SCENE_INSTALL}>
        <InstallScene />
      </TransitionSeries.Sequence>

      <TransitionSeries.Transition
        presentation={slide({ direction: "from-right" })}
        timing={transitionTiming}
      />

      <TransitionSeries.Sequence durationInFrames={SCENE_COMMANDS}>
        <CommandsScene />
      </TransitionSeries.Sequence>

      <TransitionSeries.Transition
        presentation={fade()}
        timing={transitionTiming}
      />

      <TransitionSeries.Sequence durationInFrames={SCENE_JSON}>
        <JsonScene />
      </TransitionSeries.Sequence>

      <TransitionSeries.Transition
        presentation={slide({ direction: "from-bottom" })}
        timing={transitionTiming}
      />

      <TransitionSeries.Sequence durationInFrames={SCENE_REQUIREMENTS}>
        <RequirementsScene />
      </TransitionSeries.Sequence>

      <TransitionSeries.Transition
        presentation={fade()}
        timing={transitionTiming}
      />

      <TransitionSeries.Sequence durationInFrames={SCENE_OUTRO}>
        <OutroScene />
      </TransitionSeries.Sequence>
    </TransitionSeries>
  );
};
