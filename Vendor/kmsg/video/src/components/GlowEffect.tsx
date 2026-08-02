import React from "react";
import { colors } from "../lib/theme";

interface GlowEffectProps {
  color?: string;
  x?: number;
  y?: number;
  size?: number;
  opacity?: number;
}

export const GlowEffect: React.FC<GlowEffectProps> = ({
  color = colors.accent,
  x = 540,
  y = 540,
  size = 400,
  opacity: opacityProp = 0.15,
}) => {
  return (
    <div
      style={{
        position: "absolute",
        width: size * 2,
        height: size * 2,
        left: x - size,
        top: y - size,
        borderRadius: "50%",
        background: `radial-gradient(circle, ${color} 0%, transparent 70%)`,
        opacity: opacityProp,
        pointerEvents: "none",
        filter: "blur(60px)",
      }}
    />
  );
};
