"use client"

import {
  ReactNode,
  createContext,
  useContext,
  useState,
} from "react"
import { Api } from "@/services/api.service"
import {
  SpiSystemUserUpdateRequest,
} from "@/types/api.types"

interface SpiSystemUserContextType {
  // State
  // Operations
  update: (data: SpiSystemUserUpdateRequest) => Promise<boolean>
}

// Undefined default is intentional — enforced by the useSpiSystemUser hook below.
const SpiSystemUserContext = createContext<SpiSystemUserContextType | undefined>(undefined)

export function SpiSystemUserProvider({ children }: { children: ReactNode }) {
  const [state, setState] = useState<any>({})

  const update = async (data: SpiSystemUserUpdateRequest): Promise<boolean> => {
    try {
      await Api.SPI.SystemUser.update(data)
      return true
    } catch (error) {
      console.error(error)
      throw error
    }
  }

  // No useMemo — the React Compiler handles memoization.
  return (
    <SpiSystemUserContext.Provider
      value={{
        ...state,
        update,
      }}
    >
      {children}
    </SpiSystemUserContext.Provider>
  )
}

// Throws if used outside of SpiSystemUserProvider to catch missing provider wrapping early.
export function useSpiSystemUser() {
  const context = useContext(SpiSystemUserContext)
  if (context === undefined) {
    throw new Error("useSpiSystemUser must be used within a SpiSystemUserProvider")
  }
  return context
}
