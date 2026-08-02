import React from "react";
import { useCurrentFrame, interpolate, AbsoluteFill } from "remotion";
import { colors } from "../lib/theme";
import { Terminal } from "../components/Terminal";
import { PromptLine } from "../components/PromptLine";

const commands = [
  {
    command: "kmsg status",
    output: ["\u2713 Accessibility: Granted", "\u2713 KakaoTalk: Running"],
    outputColor: colors.success,
    start: 0,
  },
  {
    command: 'kmsg send "\uD76C\uCC2C" "\uC548\uB155\uD558\uC138\uC694"',
    output: ["\u2713 Message sent to \uD76C\uCC2C"],
    outputColor: colors.success,
    start: 52,
  },
  {
    command: "kmsg chats",
    output: ["  \uD76C\uCC2C", "  \uAC00\uC871\uBC29", "  \uD68C\uC0AC\uD300"],
    start: 112,
  },
  {
    command: 'kmsg read "\uD76C\uCC2C" --limit 3',
    output: [
      "  [14:30] \uD76C\uCC2C: \uC548\uB155\uD558\uC138\uC694",
      "  [14:31] \uB098: \uB124 \uBC18\uAC11\uC2B5\uB2C8\uB2E4",
      "  [14:32] \uD76C\uCC2C: \uC624\uB298 \uD68C\uC758 \uC788\uB098\uC694?",
    ],
    start: 162,
  },
];

const BLOCK_HEIGHT = 80;
const scrollTargets = [0, -BLOCK_HEIGHT, -BLOCK_HEIGHT * 2, -BLOCK_HEIGHT * 3];

export const CommandsScene: React.FC = () => {
  const frame = useCurrentFrame();

  // Calculate scroll position: smoothly scroll up when each new command starts
  let scrollY = 0;
  for (let i = commands.length - 1; i > 0; i--) {
    if (frame >= commands[i].start - 5) {
      scrollY = interpolate(
        frame,
        [commands[i].start - 5, commands[i].start + 10],
        [scrollTargets[i - 1], scrollTargets[i]],
        { extrapolateLeft: "clamp", extrapolateRight: "clamp" },
      );
      break;
    }
  }

  return (
    <AbsoluteFill
      style={{
        backgroundColor: colors.bg,
        justifyContent: "center",
        alignItems: "center",
      }}
    >
      <Terminal>
        <div style={{ transform: `translateY(${scrollY}px)` }}>
          {commands.map((cmd, i) => {
            const visible = frame >= cmd.start;
            return (
              <div
                key={i}
                style={{ marginBottom: 16, opacity: visible ? 1 : 0 }}
              >
                <PromptLine
                  command={cmd.command}
                  output={cmd.output}
                  outputColor={cmd.outputColor}
                  startFrame={cmd.start}
                />
              </div>
            );
          })}
        </div>
      </Terminal>
    </AbsoluteFill>
  );
};
