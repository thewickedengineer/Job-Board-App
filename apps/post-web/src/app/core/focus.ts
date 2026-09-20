import { Injector, afterNextRender } from '@angular/core';

/**
 * Focus something that a signal change is about to render. With zoneless
 * change detection a microtask can run before the view updates, so the focus
 * has to wait for the next render.
 */
export function focusAfterRender(injector: Injector, find: () => HTMLElement | null | undefined): void {
  afterNextRender(() => find()?.focus(), { injector });
}
