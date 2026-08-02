import React from "react";
import { colors, layout } from "../lib/theme";
import { jetBrainsMono } from "../lib/fonts";

interface TerminalProps {
  children: React.ReactNode;
  title?: string;
  opacity?: number;
}

export const Terminal: React.FC<TerminalProps> = ({
  children,
  title = "kmsg",
  opacity = 1,
}) => {
  const dots = ["#FF5F57", "#FEBC2E", "#28C840"] as const;

  return (
    <div
      style={{
        width: layout.terminalWidth,
        borderRadius: layout.terminalBorderRadius,
        overflow: "hidden",
        opacity,
      }}
    >
      {/* Title bar */}
      <div
        style={{
          height: layout.terminalTitleBarHeight,
          backgroundColor: colors.bgTitleBar,
          display: "flex",
          alignItems: "center",
          padding: "0 16px",
          position: "relative",
        }}
      >
        <div style={{ display: "flex", gap: 8 }}>
          {dots.map((color) => (
            <div
              key={color}
              style={{
                width: 12,
                height: 12,
                borderRadius: "50%",
                backgroundColor: color,
              }}
            />
          ))}
        </div>
        <div
          style={{
            position: "absolute",
            left: 0,
            right: 0,
            textAlign: "center",
            fontFamily: jetBrainsMono,
            fontSize: 14,
            color: colors.textSecondary,
            pointerEvents: "none",
          }}
        >
          {title}
        </div>
      </div>

      {/* Body */}
      <div
        style={{
          backgroundColor: colors.bgTerminal,
          padding: 24,
          minHeight: 400,
          overflow: "hidden",
        }}
      >
        {children}
      </div>
    </div>
  );
};
