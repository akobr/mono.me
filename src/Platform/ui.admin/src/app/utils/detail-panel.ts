/**
 * How the detail beside a list opens:
 * - `overlay`: over the right part of the list, which stays usable;
 * - `pinned`: docked beside the list, which makes room for it;
 * - `full`: in place of the list, as a page of its own.
 */
export type DetailMode = 'overlay' | 'pinned' | 'full'

/** Width of the overlay and of the pinned detail, in percent of the window; the arrow keys move it by `step`. */
export const detailWidth = { min: 25, max: 70, default: 40, step: 2 } as const

export interface DetailScreen {
  pinned: boolean
  fullscreen: boolean
  /** At least md (768 px): below it, the detail is always full screen. */
  medium: boolean
  /** At least lg (1024 px): below it, there is no room for a pinned detail. */
  large: boolean
}

/** The mode the detail opens in, from the user's preferences and the screen size. */
export function detailMode({ pinned, fullscreen, medium, large }: DetailScreen): DetailMode {
  if (!medium || fullscreen) {
    return 'full'
  }
  return pinned && large ? 'pinned' : 'overlay'
}

/** A width within the limits; anything that is not a number (a damaged stored value) gives the default. */
export function clampDetailWidth(value: number): number {
  return Number.isFinite(value) ? Math.min(detailWidth.max, Math.max(detailWidth.min, value)) : detailWidth.default
}
