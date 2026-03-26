"use client"

import {
  ReactNode,
  createContext,
  useContext,
  useState,
} from "react"
import { Api } from "@/services/api.service"
import {
  SpSystemUserUpdateRequest,
} from "@/types/api.types"

interface SpSystemUserContextType {
  // State
  // Operations
  update: (data: SpSystemUserUpdateRequest) => Promise<boolean>
}

// Undefined default is intentional — enforced by the useSpSystemUser hook below.
const SpSystemUserContext = createContext<SpSystemUserContextType | undefined>(undefined)

export function SpSystemUserProvider({ children }: { children: ReactNode }) {
  const [state, setState] = useState<any>({})

  const update = async (data: SpSystemUserUpdateRequest): Promise<boolean> => {
    try {
      await Api.SP.SystemUser.update(data)
      return true
    } catch (error) {
      console.error(error)
      throw error
    }
  }

  // No useMemo — the React Compiler handles memoization.
  return (
    <SpSystemUserContext.Provider
      value={{
        ...state,
        update,
      }}
    >
      {children}
    </SpSystemUserContext.Provider>
  )
}

// Throws if used outside of SpSystemUserProvider to catch missing provider wrapping early.
export function useSpSystemUser() {
  const context = useContext(SpSystemUserContext)
  if (context === undefined) {
    throw new Error("useSpSystemUser must be used within a SpSystemUserProvider")
  }
  return context
}
