declare module "cs2/modding" {
  export type ModRegistrar = (registry: {
    append: (slot: string, component: React.ComponentType) => void;
  }) => void;
}

declare module "cs2/api" {
  export function bindValue<T>(group: string, name: string, initial: T): unknown;
  export function useValue<T>(binding: unknown): T;
  export function trigger(group: string, name: string, arg?: unknown): void;
  export function call<T>(group: string, name: string, arg?: unknown): Promise<T>;
}

declare module "cs2/l10n" {
  export function useLocalization(): { [key: string]: string };
  export const Localized: React.ComponentType<{ id: string }>;
}

declare module "cohtml/cohtml" {
  const engine: {
    call: (name: string, ...args: unknown[]) => Promise<unknown>;
    on: (name: string, cb: (...args: unknown[]) => void) => void;
    trigger: (name: string, ...args: unknown[]) => void;
  };
  export default engine;
}

declare module "*.svg" {
  const content: string;
  export default content;
}
