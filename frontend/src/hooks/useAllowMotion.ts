import { useEffect, useState } from "react";

interface NetworkInformation {
  saveData?: boolean;
}

function allowed(): boolean {
  const reduced = window.matchMedia("(prefers-reduced-motion: reduce)").matches;
  const saveData = (navigator as Navigator & { connection?: NetworkInformation }).connection?.saveData === true;
  return !reduced && !saveData;
}

/** False under prefers-reduced-motion or Save-Data: the trailer never starts (D8, NFR-11). */
export function useAllowMotion(): boolean {
  const [allow, setAllow] = useState(allowed);
  useEffect(() => {
    const query = window.matchMedia("(prefers-reduced-motion: reduce)");
    const update = () => setAllow(allowed());
    query.addEventListener("change", update);
    return () => query.removeEventListener("change", update);
  }, []);
  return allow;
}
