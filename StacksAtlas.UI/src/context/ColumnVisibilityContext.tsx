import React, { createContext, useContext, useEffect, useState } from "react";
import {
  type ColumnVisibilityMap,
  buildEssentialColumnVisibility,
} from "../components/devices/types";

const STORAGE_KEY = "columnVisibility_v2";
const defaultVisibility = buildEssentialColumnVisibility();

const ColumnVisibilityContext = createContext({
  visibility: defaultVisibility,
  setVisibility: (_v: ColumnVisibilityMap) => { },
});

export const ColumnVisibilityProvider = ({ children }: { children: React.ReactNode }) => {
  const [visibility, setVisibility] = useState<ColumnVisibilityMap>(() => {
    const saved = localStorage.getItem(STORAGE_KEY);
    if (!saved) return defaultVisibility;

    const parsed = JSON.parse(saved) as Partial<ColumnVisibilityMap>;
    return { ...defaultVisibility, ...parsed };
  });

  useEffect(() => {
    localStorage.setItem(STORAGE_KEY, JSON.stringify(visibility));
  }, [visibility]);

  return (
    <ColumnVisibilityContext.Provider value={{ visibility, setVisibility }}>
      {children}
    </ColumnVisibilityContext.Provider>
  );
};

export const useColumnVisibility = () => useContext(ColumnVisibilityContext);
