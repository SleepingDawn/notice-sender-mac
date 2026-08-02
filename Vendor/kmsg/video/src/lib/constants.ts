export const FPS = 30;
export const CANVAS_WIDTH = 1080;
export const CANVAS_HEIGHT = 1080;

export const TRANSITION_DURATION = 10;
export const TRANSITION_COUNT = 5;

// Scene durations in frames
export const SCENE_INTRO = 150;
export const SCENE_INSTALL = 165;
export const SCENE_COMMANDS = 210;
export const SCENE_JSON = 120;
export const SCENE_REQUIREMENTS = 90;
export const SCENE_OUTRO = 120;

export const TOTAL_DURATION =
  SCENE_INTRO +
  SCENE_INSTALL +
  SCENE_COMMANDS +
  SCENE_JSON +
  SCENE_REQUIREMENTS +
  SCENE_OUTRO -
  TRANSITION_COUNT * TRANSITION_DURATION;

// Typing animation speed (frames per character)
export const CHAR_FRAMES = 2;

// Cursor blink interval in frames
export const CURSOR_BLINK_FRAMES = 16;
