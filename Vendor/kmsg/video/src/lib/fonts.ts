import { loadFont as loadJetBrainsMono } from "@remotion/google-fonts/JetBrainsMono";
import { loadFont as loadNotoSansKR } from "@remotion/google-fonts/NotoSansKR";

const jetBrainsMonoFont = loadJetBrainsMono("normal", {
  weights: ["400", "700"],
  subsets: ["latin"],
});

const notoSansKRFont = loadNotoSansKR("normal", {
  weights: ["400", "700"],
  subsets: ["latin"],
});

export const jetBrainsMono = jetBrainsMonoFont.fontFamily;
export const notoSansKR = notoSansKRFont.fontFamily;
