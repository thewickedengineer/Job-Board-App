import { Component, provideZonelessChangeDetection } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { FormControl, ReactiveFormsModule } from '@angular/forms';
import { DatePickerComponent } from './date-picker.component';

@Component({
  imports: [ReactiveFormsModule, DatePickerComponent],
  template: `<app-date-picker inputId="closingDate" [formControl]="control" [min]="min" />`,
})
class HostComponent {
  readonly control = new FormControl('2026-09-25', { nonNullable: true });
  min = '2026-09-21';
}

/** The wireframe 3.2 keyboard contract: arrows, PageUp/PageDown, Home/End, Esc, Enter; disabled days skipped. */
describe('DatePickerComponent', () => {
  let fixture: ComponentFixture<HostComponent>;
  let host: HTMLElement;

  const trigger = () => host.querySelector<HTMLButtonElement>('button[aria-haspopup="dialog"]')!;
  const grid = () => host.querySelector<HTMLElement>('[role="dialog"]');
  const focusedIso = () => (document.activeElement as HTMLElement | null)?.dataset['iso'];
  const key = (k: string) => {
    grid()!.dispatchEvent(new KeyboardEvent('keydown', { key: k, bubbles: true }));
  };
  const settle = async () => {
    await fixture.whenStable();
    await new Promise((r) => setTimeout(r, 0)); // focusCell runs in a microtask after render
  };

  beforeEach(async () => {
    await TestBed.configureTestingModule({ imports: [HostComponent], providers: [provideZonelessChangeDetection()] }).compileComponents();
    fixture = TestBed.createComponent(HostComponent);
    host = fixture.nativeElement;
    document.body.appendChild(host);
    await fixture.whenStable();
  });

  afterEach(() => host.remove());

  it('opens on the trigger, focuses the selected day, and closes on Escape returning focus to the trigger', async () => {
    trigger().click();
    await settle();

    expect(grid()).not.toBeNull();
    expect(focusedIso()).toBe('2026-09-25');

    key('Escape');
    await settle();
    expect(grid()).toBeNull();
    expect(document.activeElement).toBe(trigger());
  });

  it('moves with arrows, Home/End and PageDown, and never lands on a disabled day', async () => {
    trigger().click();
    await settle();

    key('ArrowRight'); await settle();
    expect(focusedIso()).toBe('2026-09-26');

    key('ArrowDown'); await settle();
    expect(focusedIso()).toBe('2026-10-03');

    key('Home'); await settle(); // Monday of that week
    expect(focusedIso()).toBe('2026-09-28');

    key('End'); await settle(); // Sunday of that week
    expect(focusedIso()).toBe('2026-10-04');

    key('PageDown'); await settle();
    expect(focusedIso()).toBe('2026-11-04');

    // Way back past the minimum: focus is clamped to the first enabled day.
    for (let i = 0; i < 3; i++) { key('PageUp'); await settle(); }
    expect(focusedIso()).toBe('2026-09-21');
  });

  it('writes the focused day into the form control on Enter', async () => {
    trigger().click();
    await settle();
    key('ArrowLeft'); await settle();
    key('Enter'); await settle();

    expect(fixture.componentInstance.control.value).toBe('2026-09-24');
    expect(grid()).toBeNull();
  });

  it('marks days before min as aria-disabled and ignores clicks on them', async () => {
    trigger().click();
    await settle();

    const past = host.querySelector<HTMLButtonElement>('[data-iso="2026-09-20"]')!;
    expect(past.getAttribute('aria-disabled')).toBe('true');
    past.click();
    await settle();
    expect(fixture.componentInstance.control.value).toBe('2026-09-25');
    expect(grid()).not.toBeNull();
  });
});
