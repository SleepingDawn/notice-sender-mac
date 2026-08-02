import React from "react";
import { useCurrentFrame, interpolate } from "remotion";
import { colors } from "../lib/theme";
import { jetBrainsMono } from "../lib/fonts";

interface JsonBlockProps {
  json: string;
  startFrame?: number;
  lineDelay?: number;
}

const highlightJson = (line: string): React.ReactNode[] => {
  const parts: React.ReactNode[] = [];
  const regex =
    /("(?:[^"\\]|\\.)*")\s*:|("(?:[^"\\]|\\.)*")|([{}\[\]])|([,:])|(-?\d+(?:\.\d+)?(?:e[+-]?\d+)?)|(\s+)/gi;
  let lastIndex = 0;
  let match: RegExpExecArray | null;
  let keyIndex = 0;

  while ((match = regex.exec(line)) !== null) {
    // Push any unmatched text before this match
    if (match.index > lastIndex) {
      parts.push(
        <span key={`t-${keyIndex++}`}>
          {line.slice(lastIndex, match.index)}
        </span>,
      );
    }

    if (match[1]) {
      // Key (quoted string before colon)
      parts.push(
        <span key={`k-${keyIndex++}`} style={{ color: colors.syntaxKey }}>
          {match[1]}
        </span>,
      );
      // Include the colon and whitespace that follows the key
      const colonPart = match[0].slice(match[1].length);
      parts.push(
        <span key={`c-${keyIndex++}`} style={{ color: colors.textSecondary }}>
          {colonPart}
        </span>,
      );
    } else if (match[2]) {
      // String value
      parts.push(
        <span key={`s-${keyIndex++}`} style={{ color: colors.syntaxString }}>
          {match[2]}
        </span>,
      );
    } else if (match[3]) {
      // Braces / brackets
      parts.push(
        <span key={`b-${keyIndex++}`} style={{ color: colors.syntaxBrace }}>
          {match[3]}
        </span>,
      );
    } else if (match[4]) {
      // Commas, colons
      parts.push(
        <span key={`p-${keyIndex++}`} style={{ color: colors.textSecondary }}>
          {match[4]}
        </span>,
      );
    } else if (match[5]) {
      // Numbers
      parts.push(
        <span key={`n-${keyIndex++}`} style={{ color: colors.syntaxString }}>
          {match[5]}
        </span>,
      );
    } else if (match[6]) {
      // Whitespace
      parts.push(<span key={`w-${keyIndex++}`}>{match[6]}</span>);
    }

    lastIndex = match.index + match[0].length;
  }

  // Push any remaining text
  if (lastIndex < line.length) {
    parts.push(<span key={`r-${keyIndex++}`}>{line.slice(lastIndex)}</span>);
  }

  return parts;
};

export const JsonBlock: React.FC<JsonBlockProps> = ({
  json,
  startFrame = 0,
  lineDelay = 4,
}) => {
  const frame = useCurrentFrame();
  const lines = json.split("\n");

  return (
    <div
      style={{
        fontFamily: jetBrainsMono,
        fontSize: 20,
        lineHeight: 1.6,
        whiteSpace: "pre",
      }}
    >
      {lines.map((line, index) => {
        const lineStart = startFrame + index * lineDelay;
        const opacity = interpolate(frame, [lineStart, lineStart + 6], [0, 1], {
          extrapolateLeft: "clamp",
          extrapolateRight: "clamp",
        });

        return (
          <div key={index} style={{ opacity }}>
            {highlightJson(line)}
          </div>
        );
      })}
    </div>
  );
};
