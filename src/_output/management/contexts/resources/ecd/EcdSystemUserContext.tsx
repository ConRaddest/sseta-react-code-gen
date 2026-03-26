"use client"

import {
  ReactNode,
  createContext,
  useContext,
  useState,
} from "react"
import { Api } from "@/services/api.service"
import {
  EcdSystemUserUpdateRequest,
} from "@/types/api.types"

interface EcdSystemUserContextType {
  // State
  // Operations
  update: (data: EcdSystemUserUpdateRequest) => Promise<boolean>
}

// Undefined default is intentional — enforced by the useEcdSystemUser hook below.
const EcdSystemUserContext = createContext<EcdSystemUserContextType | undefined>(undefined)

export function EcdSystemUserProvider({ children }: { children: ReactNode }) {
  const [state, setState] = useState<any>({})

  const update = async (data: EcdSystemUserUpdateRequest): Promise<boolean> => {
    try {
      await Api.ECD.SystemUser.update(data)
      return true
    } catch (error) {
      console.error(error)
      throw error
    }
  }

  // No useMemo — the React Compiler handles memoization.
  return (
    <EcdSystemUserContext.Provider
      value={{
        ...state,
        update,
      }}
    >
      {children}
    </EcdSystemUserContext.Provider>
  )
}

// Throws if used outside of EcdSystemUserProvider to catch missing provider wrapping early.
export function useEcdSystemUser() {
  const context = useContext(EcdSystemUserContext)
  if (context === undefined) {
    throw new Error("useEcdSystemUser must be used within a EcdSystemUserProvider")
  }
  return context
}
